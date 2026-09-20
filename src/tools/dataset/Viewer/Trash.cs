using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace JassCardEye.Dataset.Viewer;

/// <summary>
/// Removes files the way Finder does: into the Trash, from where a frame deleted by mistake can be put
/// back. macOS ships a <c>trash</c> command for exactly that; where there is none, the files are deleted
/// for good, and <see cref="IsAvailable"/> lets the screen say so beforehand.
/// </summary>
internal static class Trash
{
    private const string Command = "/usr/bin/trash";

    /// <summary>True when removed files can still be recovered from the Trash.</summary>
    public static bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists(Command);

    public static void Move(IReadOnlyCollection<string> files)
    {
        if (files.Count == 0) return;

        if (!IsAvailable)
        {
            foreach (var file in files) File.Delete(file);
            return;
        }

        // In batches, so a few thousand frames do not run into the length limit of an argument list.
        foreach (var batch in files.Chunk(200))
        {
            var start = new ProcessStartInfo(Command)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var file in batch) start.ArgumentList.Add(file);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("trash could not be started.");
            process.StandardOutput.ReadToEnd();
            var errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new IOException(errors.Trim().Length > 0 ? errors.Trim() : $"trash stopped with exit code {process.ExitCode}.");
        }
    }
}
