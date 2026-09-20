using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JassCardEye.Dataset.Cards;
using JassCardEye.Dataset.Generation;
using SkiaSharp;

namespace JassCardEye.Dataset.Viewer.Predict;

/// <summary>
/// Proposes a label for a photo with the two stages of variant B: B₁ finds the oriented box - the
/// four corners a label consists of - and B₂ names the card on the rectified crop.
///
/// The models run in a Python process (<c>src/training/propose.py</c>) out of the training
/// environment, started once and kept alive, because loading two sets of weights takes seconds and a
/// person labelling presses ⇧→ every few seconds. The tool brings no inference of its own along, the
/// same way it brings no ffmpeg: what is not installed simply switches the feature off.
///
/// Two things this class insists on, and both are about measuring:
///
/// - The models are asked about the **square the dataset stores**, never the raw photo, so they see
///   the pictures they were trained on and the corners come back in a frame that maps cleanly onto
///   the photo (<see cref="PhotoSquare"/>).
/// - The crop is rectified **here**, with <c>CardCrop.Rectify</c> - the very code that produced B₂'s
///   training crops. A rectification of its own in the helper would be a second opinion about what a
///   card looks like.
/// </summary>
internal sealed class CardProposer : IDisposable
{
    /// <summary>The side of the square the dataset stores for a photo (VariantDatasetWriter, 640).</summary>
    private const int SquareSize = 640;

    /// <summary>Loading torch and two sets of weights, cold, on a laptop.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);

    /// <summary>One image through one model. Generous: an answer that slow is a fault, not patience.</summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

    private readonly string _python, _script, _b1, _b2, _work;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<string> _stderr = new();

    private Process? _process;
    private StreamWriter? _requests;
    private StreamReader? _answers;

    private CardProposer(string python, string script, string b1, string b2, string weightsId)
    {
        (_python, _script, _b1, _b2, WeightsId) = (python, script, b1, b2, weightsId);
        _work = Path.Combine(Path.GetTempPath(), $"jasscardeye-propose-{Environment.ProcessId}");
    }

    /// <summary>Which weights the proposals come from - the run id, written into the proposal log.</summary>
    public string WeightsId { get; }

    /// <summary>Both weight files, for the tooltip: a proposal should never come from a mystery.</summary>
    public string Details => $"{_b1}\n{_b2}\n{_python}";

    /// <summary>
    /// Looks for everything a proposal needs, and says what is missing when it is not all there. Only
    /// the search happens here - no process is started until the first proposal is asked for, so a
    /// labelling session that never presses ⇧→ never loads a model.
    /// </summary>
    public static (CardProposer? Proposer, string Status) Discover()
    {
        if (FindRepository() is not { } repo)
            return (null, "✗ src/training/propose.py was not found next to this build");

        var python = Environment.GetEnvironmentVariable("JASSCARDEYE_PROPOSE_PYTHON") ?? DefaultPython(repo);
        if (!File.Exists(python))
            return (null, "✗ no Python environment with Ultralytics (src/training/.venv) – see src/training/README.md");

        var (b1, b2, weightsId) = FindWeights(repo);
        if (b1 is null || b2 is null)
            return (null, "✗ no B₁/B₂ weights – src/training/fetch_run.py --run-id <run> --variant b1 --weights-only");

        var script = Path.Combine(repo, "src", "training", "propose.py");
        return (new CardProposer(python, script, b1, b2, weightsId), $"✓ B₁ and B₂ from {weightsId}");
    }

    /// <summary>
    /// A label for the photo at <paramref name="photoPath"/>, or null when B₁ sees no card worth
    /// proposing. The photo stays untouched: what comes back is a suggestion in its pixel
    /// coordinates, nothing is written.
    ///
    /// The photo is read from disk here rather than handed in: the window reloads and disposes its
    /// own copy whenever it moves on, and a proposal is asked for exactly when someone is moving on.
    /// </summary>
    public async Task<Proposal?> ProposeAsync(string photoPath, Deck deck, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);   // one image at a time: one process, one conversation
        try
        {
            await EnsureStartedAsync(cancellation);
            Directory.CreateDirectory(_work);

            using var photo = SKImage.FromEncodedData(SKData.Create(photoPath))
                ?? throw new InvalidOperationException($"{Path.GetFileName(photoPath)} could not be decoded");
            var (square, _) = VariantDatasetWriter.CenterCropSquare(photo, [], SquareSize);
            using (square)
            {
                var squareFile = Write(square, SKEncodedImageFormat.Jpeg, 90, "square.jpg");
                var boxes = await LocateAsync(squareFile, cancellation);
                if (ProposalRules.PickBox(boxes) is not { } picked) return null;
                var (box, boxNote) = picked;

                var corners = ObbCorners.Upright(box.Corners);
                var scores = await ClassifyAsync(square, corners, cancellation);
                var (card, confidence, cardNote) = ProposalRules.PickCard(scores, deck);

                var toPhoto = PhotoSquare.Of(photo.Width, photo.Height, SquareSize);
                return new Proposal(
                    [.. corners.Select(toPhoto.ToPhoto)], box.Confidence, card, confidence,
                    ProposalRules.Join(boxNote, cardNote));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<BoxScore>> LocateAsync(string image, CancellationToken cancellation)
    {
        using var answer = await AskAsync(new { op = "locate", image }, cancellation);
        var boxes = new List<BoxScore>();
        if (!answer.RootElement.TryGetProperty("boxes", out var list)) return boxes;

        foreach (var entry in list.EnumerateArray())
        {
            var corners = entry.GetProperty("corners").EnumerateArray()
                .Select(p => new Vector2(p[0].GetSingle(), p[1].GetSingle())).ToArray();
            if (corners.Length == 4)
                boxes.Add(new BoxScore(corners, entry.GetProperty("confidence").GetSingle()));
        }
        return boxes;
    }

    private async Task<IReadOnlyList<ClassScore>> ClassifyAsync(
        SKImage square, Vector2[] corners, CancellationToken cancellation)
    {
        using var crop = CardCrop.Rectify(square, corners);
        var image = Write(crop, SKEncodedImageFormat.Png, 100, "crop.png");

        using var answer = await AskAsync(new { op = "classify", image }, cancellation);
        return answer.RootElement.TryGetProperty("classes", out var list)
            ? [.. list.EnumerateArray().Select(c =>
                new ClassScore(c.GetProperty("name").GetString() ?? "", c.GetProperty("confidence").GetSingle()))]
            : [];
    }

    private string Write(SKImage image, SKEncodedImageFormat format, int quality, string name)
    {
        var path = Path.Combine(_work, name);
        using var data = image.Encode(format, quality);
        using var file = File.Create(path);
        data.SaveTo(file);
        return path;
    }

    // --- The process ---

    private async Task EnsureStartedAsync(CancellationToken cancellation)
    {
        if (_process is { HasExited: false }) return;

        var start = new ProcessStartInfo(_python)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { _script, "--serve", "--b1", _b1, "--b2", _b2 })
            start.ArgumentList.Add(argument);

        _process = Process.Start(start) ?? throw new InvalidOperationException($"{_python} did not start");
        _requests = _process.StandardInput;
        _answers = _process.StandardOutput;

        // Everything the models print goes to stderr by design; it is kept only to explain a failure.
        var errors = _process.StandardError;
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await errors.ReadLineAsync()) is not null)
            {
                lock (_stderr)
                {
                    _stderr.Enqueue(line);
                    while (_stderr.Count > 20) _stderr.Dequeue();
                }
            }
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(StartTimeout);
        var ready = await ReadLineAsync(timeout.Token);
        using var document = JsonDocument.Parse(ready);
        if (!document.RootElement.TryGetProperty("ready", out var flag) || !flag.GetBoolean())
            throw Failed("the models did not load");
    }

    private async Task<JsonDocument> AskAsync(object request, CancellationToken cancellation)
    {
        if (_requests is null) throw Failed("no proposal process");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(AnswerTimeout);

        await _requests.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
        await _requests.FlushAsync(timeout.Token);

        var document = JsonDocument.Parse(await ReadLineAsync(timeout.Token));
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            document.Dispose();
            throw Failed(error.GetString() ?? "unknown error");
        }
        return document;
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellation) =>
        _answers is null ? throw Failed("no proposal process")
            : await _answers.ReadLineAsync(cancellation) ?? throw Failed("the proposal process ended");

    // The last thing the process said is the only clue to why it stopped - a missing package, weights
    // of the wrong task - so it travels with the message instead of into a log nobody opens.
    private InvalidOperationException Failed(string what)
    {
        string tail;
        lock (_stderr) tail = string.Join(" | ", _stderr.TakeLast(3));
        return new InvalidOperationException(tail.Length > 0 ? $"{what} ({tail})" : what);
    }

    public void Dispose()
    {
        try
        {
            // Closing stdin ends the script's loop; killing is for a process that ignores that.
            _requests?.Close();
            if (_process is { HasExited: false } process && !process.WaitForExit(2000)) process.Kill(true);
            _process?.Dispose();
        }
        catch (Exception)
        {
            // Shutting down is not a moment to fail in.
        }
        finally
        {
            try { if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true); }
            catch (Exception) { /* a temp folder left behind is not worth a message */ }
            _gate.Dispose();
        }
    }

    // --- Where things are ---

    // The repository the build came out of: the helper script and the weights live in it, and the
    // tool is run from it (dotnet run) or out of its bin folder.
    private static string? FindRepository()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "src", "training", "propose.py")))
                    return dir.FullName;
        }
        return null;
    }

    private static string DefaultPython(string repo) => Path.Combine(repo, "src", "training", ".venv",
        OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python");

    /// <summary>
    /// The weights to propose from: a fetched run that has both stages, newest first, else a training
    /// run made by hand. Both can be overridden - a run downloaded somewhere else, or an older one to
    /// compare against.
    /// </summary>
    private static (string? B1, string? B2, string Id) FindWeights(string repo)
    {
        var b1 = Environment.GetEnvironmentVariable("JASSCARDEYE_PROPOSE_B1");
        var b2 = Environment.GetEnvironmentVariable("JASSCARDEYE_PROPOSE_B2");
        if (File.Exists(b1) && File.Exists(b2)) return (b1, b2, RunOf(b1!));

        foreach (var folder in RunFolders(repo))
        {
            var one = Path.Combine(folder, "b1", "weights", "best.pt");
            var two = Path.Combine(folder, "b2", "weights", "best.pt");
            if (File.Exists(one) && File.Exists(two)) return (one, two, Path.GetFileName(folder));
        }
        return (null, null, "");
    }

    // <run>/<variant>/weights/best.pt - the run a set of weights belongs to is its folder's grandparent.
    private static string RunOf(string weights) =>
        new FileInfo(weights).Directory?.Parent?.Parent?.Name ?? "unknown";

    private static IEnumerable<string> RunFolders(string repo)
    {
        var runs = Path.Combine(repo, "output", "runs");
        if (Directory.Exists(runs))
            foreach (var folder in new DirectoryInfo(runs).GetDirectories()
                         .OrderByDescending(d => d.LastWriteTimeUtc).Select(d => d.FullName))
                yield return folder;

        // Where training by hand puts its results: output/training/<variant>/weights/best.pt.
        yield return Path.Combine(repo, "output", "training");
    }
}
