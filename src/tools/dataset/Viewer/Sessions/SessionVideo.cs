using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JassCardEye.Dataset.Viewer.Sessions;

/// <summary>
/// The video of a recorded session, read through the ffmpeg installed on the machine - the one Extract
/// frames already needs, so looking at a session brings no dependency of its own and works wherever the
/// tool runs.
///
/// Row n of the recognition log belongs to frame n of the video in display order. ffprobe lists the
/// presentation time of every packet without decoding anything; sorted, those are the frames in display
/// order. The sorting matters: the iPhone's encoder reorders frames, so the order in the file is not the
/// order on screen.
///
/// A frame is fetched by seeking to a tenth of a millisecond before its presentation time. ffmpeg decodes
/// from the keyframe before it - both recorders write one a second - and drops every frame earlier than
/// the seek point, so the first frame out is the one asked for. This can be checked against a recording
/// whose frames show their own number (<c>CHECK_RECORDER_KEEP=1</c> in src/tools/check_recorder.swift
/// keeps one); both recorders keep frames at least a millisecond apart for it.
/// </summary>
internal sealed class SessionVideo
{
    private const double SeekLeadSeconds = 0.0001;

    // The log writes times with three decimals and a container rounds them to its own clock (1/600 s in
    // a MOV, finer in an MP4); neighbouring frames are at least a millisecond apart.
    private const double ToleranceMilliseconds = 0.5;

    private readonly string _ffmpeg;
    private readonly double _startSeconds;
    private readonly double[] _seconds;

    public string Path { get; }
    public int FrameCount => _seconds.Length;

    private SessionVideo(string ffmpeg, string path, double startSeconds, double[] seconds)
    {
        _ffmpeg = ffmpeg;
        Path = path;
        _startSeconds = startSeconds;
        _seconds = seconds;
    }

    /// <summary>Lists the frames of <paramref name="path"/>. ffprobe is looked for next to ffmpeg.</summary>
    public static async Task<SessionVideo> OpenAsync(string ffmpeg, string path, CancellationToken cancellation)
    {
        var ffprobe = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ffmpeg)!,
            OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(ffprobe))
            throw new FileNotFoundException($"ffprobe was not found next to {ffmpeg} - it comes with ffmpeg.");

        var output = await RunAsync(ffprobe,
            ["-v", "error", "-select_streams", "v:0",
             "-show_entries", "stream=time_base,start_time:packet=pts", "-of", "json", path],
            cancellation);
        using var json = JsonDocument.Parse(output);

        var stream = json.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault();
        if (stream.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The file has no video stream.");
        var timeBase = stream.GetProperty("time_base").GetString()!.Split('/');
        double unit = double.Parse(timeBase[0], CultureInfo.InvariantCulture) /
                      double.Parse(timeBase[1], CultureInfo.InvariantCulture);
        double start = stream.TryGetProperty("start_time", out var startTime) &&
                       double.TryParse(startTime.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : 0;

        var seconds = json.RootElement.GetProperty("packets").EnumerateArray()
            .Select(packet => packet.TryGetProperty("pts", out var pts)
                ? pts.GetInt64() * unit
                : throw new InvalidDataException("A frame of the video has no presentation time."))
            .Order()
            .ToArray();
        if (seconds.Length == 0) throw new InvalidDataException("The video has no frames.");
        return new SessionVideo(ffmpeg, path, start, seconds);
    }

    /// <summary>Throws unless the log has one row per frame, each at its frame's time.</summary>
    public void CheckAgainst(IReadOnlyList<SessionFrame> frames)
    {
        if (frames.Count != _seconds.Length)
            throw new InvalidDataException(
                $"the video has {_seconds.Length} frames and the log {frames.Count} rows, so they do not belong together.");
        for (int i = 0; i < _seconds.Length; i++)
        {
            double video = _seconds[i] * 1000;
            if (Math.Abs(video - frames[i].Milliseconds) > ToleranceMilliseconds)
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"frame {i + 1} is at {video:0.000} ms in the video and at {frames[i].Milliseconds:0.000} ms in the log."));
        }
    }

    /// <summary>Frame <paramref name="index"/> (from 0) as a near-lossless JPEG, the quality Extract frames writes.</summary>
    public Task<byte[]> ReadFrameAsync(int index, CancellationToken cancellation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _seconds.Length);

        // ffmpeg counts -ss from the start of the file, not from time zero.
        double seek = Math.Max(0, _seconds[index] - _startSeconds - SeekLeadSeconds);
        return RunAsync(_ffmpeg,
            ["-hide_banner", "-nostdin", "-v", "error",
             "-ss", seek.ToString("0.000000", CultureInfo.InvariantCulture), "-i", Path,
             "-frames:v", "1", "-q:v", "2", "-f", "image2pipe", "-c:v", "mjpeg", "-"],
            cancellation);
    }

    /// <summary>Runs a tool and returns what it wrote to stdout; its last error line when it fails.</summary>
    private static async Task<byte[]> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        var name = System.IO.Path.GetFileNameWithoutExtension(executable);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{name} could not be started.");
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var output = new MemoryStream();
        using (cancellation.Register(() =>
               {
                   try { process.Kill(entireProcessTree: true); }
                   catch (InvalidOperationException) { /* already gone */ }
               }))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(output, CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            var lastLine = (await errors).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            throw new InvalidOperationException(lastLine ?? $"{name} stopped with exit code {process.ExitCode}.");
        }
        return output.ToArray();
    }
}
