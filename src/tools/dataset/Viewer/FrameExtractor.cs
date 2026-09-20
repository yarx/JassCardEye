using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JassCardEye.Dataset.Viewer;

/// <summary>
/// Turns a video into photos to label: every n-th frame, as JPEGs in a folder named after the video,
/// next to it. The work is done by the ffmpeg installed on the machine; the viewer brings none along.
///
/// A frame is named after the video and its frame number (<c>game_1_00005.jpg</c>). The name alone
/// then says which video a photo came from; ffmpeg's own numbering restarts at 1 for every video, so
/// without the video's name the frames of two videos would collide.
/// </summary>
internal static class FrameExtractor
{
    public readonly record struct Result(string Folder, int Frames);

    // A GUI app started from Finder or the Dock does not inherit the shell's PATH, so the folders a
    // package manager installs into are searched as well.
    private static readonly string[] KnownFolders = ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"];

    /// <summary>The ffmpeg executable, or <c>null</c> when none is installed.</summary>
    public static string? FindFfmpeg()
    {
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(KnownFolders);

        foreach (var folder in folders)
        {
            var candidate = Path.Combine(folder, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The folder the frames of a video go to: beside it, named after it.</summary>
    public static string TargetFolder(string videoPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(videoPath))!, Path.GetFileNameWithoutExtension(videoPath));

    /// <summary>
    /// Extracts frames 1, 1 + step, 1 + 2·step, … of <paramref name="videoPath"/> into a new folder
    /// beside it. The folder must not exist yet or be empty: frames of two extractions with different
    /// steps would otherwise sit side by side with nothing to tell them apart.
    /// </summary>
    public static async Task<Result> ExtractAsync(
        string ffmpeg, string videoPath, int step, IProgress<int>? progress, CancellationToken cancellation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(step, 1);

        var name = Path.GetFileNameWithoutExtension(videoPath);
        var folder = TargetFolder(videoPath);

        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException($"{folder} already exists and is not empty - remove or rename it first.");

        bool createdFolder = !Directory.Exists(folder);
        // ffmpeg numbers its output 1, 2, 3 …; the frames get their real numbers only once it is done.
        // Until then they wait in a hidden folder, so a cancelled run cannot leave misnamed photos.
        var partial = Path.Combine(folder, ".extracting");
        Directory.CreateDirectory(partial);

        try
        {
            await RunFfmpegAsync(ffmpeg, videoPath, step, partial, progress, cancellation);

            var extracted = Directory.GetFiles(partial, "*.jpg").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (extracted.Length == 0)
                throw new InvalidDataException("ffmpeg finished without writing a single frame.");

            // Output k (from 0) is frame k·step of the video, counted from 0 by ffmpeg's select filter;
            // the name counts from 1, like ffmpeg's own numbering and every video player.
            long last = (long)(extracted.Length - 1) * step + 1;
            var format = "D" + Math.Max(5, last.ToString(CultureInfo.InvariantCulture).Length);
            for (int k = 0; k < extracted.Length; k++)
            {
                long frame = (long)k * step + 1;
                var target = Path.Combine(folder, $"{name}_{frame.ToString(format, CultureInfo.InvariantCulture)}.jpg");
                File.Move(extracted[k], target);
            }

            Directory.Delete(partial, recursive: true);
            return new Result(folder, extracted.Length);
        }
        catch
        {
            // Nothing half-done stays behind - not the hidden folder, and not an empty one named after
            // the video that would block the next attempt.
            TryDelete(partial);
            if (createdFolder && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                TryDelete(folder);
            throw;
        }
    }

    private static async Task RunFfmpegAsync(
        string ffmpeg, string videoPath, int step, string outputDir, IProgress<int>? progress, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        string[] arguments =
        [
            "-hide_banner", "-nostdin", "-loglevel", "error",
            "-progress", "pipe:1",                          // "frame=123" lines on stdout, for the status
            "-i", videoPath,
            "-vf", $"select=not(mod(n\\,{step}))",          // keep frames 0, step, 2·step … and drop the rest
            "-fps_mode", "vfr",                             // write what is kept, without duplicating frames to fill gaps
            "-q:v", "2",                                    // near-lossless JPEG; the dataset writer re-encodes anyway
            Path.Combine(outputDir, "%07d.jpg"),
        ];
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg could not be started.");
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var kill = cancellation.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already gone */ }
        });

        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (line.StartsWith("frame=", StringComparison.Ordinal) &&
                int.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
                progress?.Report(frames);
        }
        await process.WaitForExitAsync(CancellationToken.None);

        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            // ffmpeg's last error line is the one that says what went wrong.
            var lastLine = (await errors).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            throw new InvalidOperationException(lastLine ?? $"ffmpeg stopped with exit code {process.ExitCode}.");
        }
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
