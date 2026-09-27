using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JassCardEye.Dataset.Viewer.Simulate;

/// <summary>What a counting sequence is rendered from - the parameters of <c>scene.py</c>.</summary>
internal sealed record SimulationParams(
    string Deck, int Played, string Table, string Light, bool Hand, string Camera, int Seed, string Engine)
{
    /// <summary>A folder name that says what was rendered, so a render can be opened again and recognised.</summary>
    public string Stem => string.Create(CultureInfo.InvariantCulture,
        $"{Deck}-{Played}-{Table}-{Light}-{(Hand ? "hand" : "nohand")}-{Camera}-{Engine}-seed{Seed}");
}

/// <summary>One rendered frame and its label, as <c>render_table.py</c> wrote it into frames.json.</summary>
internal sealed record SimulatedFrame(
    int Frame, double Time, string Kind, string Reason, string? Card, Vector2[]? Corners, double? Visibility,
    double? Travel, string? Motion, bool FlyingCard, string? ImagePath);

/// <summary>A rendered counting sequence: its frames, what rendered them, and the scene they came from.</summary>
internal sealed record SimulatedSequence(
    string Folder, IReadOnlyList<SimulatedFrame> Frames, string Device, string Engine, IReadOnlyList<double> RenderSeconds,
    double TiltDegrees, int Size, string Deck, int Played, int Seed)
{
    /// <summary>Reads a folder the renderer wrote: frames.json and the JPEGs next to it.</summary>
    public static SimulatedSequence Load(string folder)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "frames.json")));
        var root = document.RootElement;
        var parameters = root.GetProperty("scene").GetProperty("params");
        var frames = new List<SimulatedFrame>();
        foreach (var f in root.GetProperty("frames").EnumerateArray())
        {
            Vector2[]? corners = f.TryGetProperty("corners", out var c)
                ? c.EnumerateArray().Select(p => new Vector2(p[0].GetSingle(), p[1].GetSingle())).ToArray()
                : null;
            string? image = f.TryGetProperty("image", out var i) ? Path.Combine(Path.GetDirectoryName(folder)!, i.GetString()!) : null;
            frames.Add(new SimulatedFrame(
                f.GetProperty("frame").GetInt32(), f.GetProperty("time").GetDouble(), f.GetProperty("kind").GetString()!,
                f.GetProperty("reason").GetString()!, f.TryGetProperty("card", out var card) ? card.GetString() : null, corners,
                Number(f, "visibility"), Number(f, "travel"), f.TryGetProperty("motion", out var m) ? m.GetString() : null,
                f.TryGetProperty("flying_card", out var fly) && fly.GetBoolean(), image));
        }
        return new SimulatedSequence(
            folder, frames, root.GetProperty("device").GetString()!, root.GetProperty("engine").GetString()!,
            root.GetProperty("render_seconds").EnumerateArray().Select(s => s.GetDouble()).ToList(),
            root.GetProperty("scene").GetProperty("camera").GetProperty("tilt").GetDouble() * 180 / Math.PI,
            parameters.GetProperty("size").GetInt32(), parameters.GetProperty("deck").GetString()!,
            parameters.GetProperty("played").GetInt32(), parameters.GetProperty("seed").GetInt32());
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    /// <summary>Frames per second of the renderer alone - the first frame, which compiles shaders, left out.</summary>
    public double? RenderRate => RenderSeconds.Count > 1 ? (RenderSeconds.Count - 1) / RenderSeconds.Skip(1).Sum() : null;
}

/// <summary>
/// Runs Blender without a window on <c>src/tools/render3d/render_table.py</c>. The renderer stays a
/// separate process: the tool only writes what to render and shows what comes back.
/// </summary>
internal static class TableSimulation
{
    private static readonly string[] KnownBlenders =
    [
        "/Applications/Blender.app/Contents/MacOS/Blender",
        "/usr/bin/blender",
        "/snap/bin/blender",
        @"C:\Program Files\Blender Foundation\Blender\blender.exe",
    ];

    /// <summary>$BLENDER, then the usual install places, then the path.</summary>
    public static string? FindBlender()
    {
        if (Environment.GetEnvironmentVariable("BLENDER") is { Length: > 0 } explicitPath && File.Exists(explicitPath))
            return explicitPath;
        foreach (var known in KnownBlenders)
            if (File.Exists(known)) return known;
        string exe = OperatingSystem.IsWindows() ? "blender.exe" : "blender";
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The repository the tool runs from: the renderer and the card scans live in it.</summary>
    public static string? FindRepository()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "src", "tools", "render3d", "render_table.py")))
                    return dir.FullName;
        return null;
    }

    /// <summary>Where the renders land: the git-ignored output folder of the repository.</summary>
    public static string RendersFolder(string repository) => Path.Combine(repository, "output", "simulate");

    /// <summary>
    /// Renders every frame of one sequence into <c>output/simulate/&lt;stem&gt;/</c> and loads the result.
    /// Progress reports "frame n of m"; cancelling stops Blender.
    /// </summary>
    public static async Task<SimulatedSequence> RenderAsync(
        string blender, string repository, SimulationParams p, IProgress<string> progress, CancellationToken cancellation)
    {
        string output = RendersFolder(repository);
        string folder = Path.Combine(output, p.Stem);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(output);

        string job = Path.Combine(output, p.Stem + ".job.json");
        var description = new
        {
            cards_root = Path.Combine(repository, "data", "cards"),
            output,
            engine = p.Engine,
            samples = p.Engine == "eevee" ? 16 : 32,
            sequences = new[]
            {
                new
                {
                    stem = p.Stem,
                    @params = new { deck = p.Deck, played = p.Played, laid = 1, table = p.Table, light = p.Light, hand = p.Hand, camera = p.Camera, seed = p.Seed },
                },
            },
        };
        await File.WriteAllTextAsync(job, JsonSerializer.Serialize(description), cancellation);

        var start = new ProcessStartInfo(blender)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "--background", "--factory-startup", "--python",
                     Path.Combine(repository, "src", "tools", "render3d", "render_table.py"), "--", job })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Blender did not start.");
        var errors = new List<string>();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (errors) errors.Add(e.Data); };
        process.BeginErrorReadLine();
        using (cancellation.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }))
        {
            while (await process.StandardOutput.ReadLineAsync(cancellation) is { } line)
                if (line.StartsWith("render3d: frame ", StringComparison.Ordinal))
                    progress.Report(line["render3d: ".Length..]);
            await process.WaitForExitAsync(cancellation);
        }
        File.Delete(job);
        if (process.ExitCode != 0)
        {
            string tail = string.Join('\n', errors.TakeLast(12));
            throw new InvalidOperationException($"Blender stopped with exit code {process.ExitCode}.\n{tail}");
        }
        return SimulatedSequence.Load(folder);
    }
}
