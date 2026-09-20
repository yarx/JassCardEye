using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace JassCardEye.Dataset.Viewer.Sessions;

/// <summary>
/// A session as both apps pack it: <c>session_&lt;time&gt;_&lt;discipline&gt;.zip</c>, holding a folder
/// of the same name with the video, the recognition log and the session info. It is opened by unpacking that
/// folder beside the archive, once; opening it again finds the folder and uses it, so frames labelled from it
/// and the extracted files stay where they were.
/// </summary>
internal static class SessionArchive
{
    /// <summary>The video of the session packed in <paramref name="zipPath"/>, unpacked beside the archive.</summary>
    public static string Unpack(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        var parent = Path.GetDirectoryName(Path.GetFullPath(zipPath))!;
        var folder = Path.Combine(parent, name);

        if (!Directory.Exists(folder))
        {
            // Unpacked under a hidden name first, so an archive that breaks halfway leaves no folder that
            // looks like a session.
            var partial = Path.Combine(parent, $".{name}.unpacking");
            if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
            ZipFile.ExtractToDirectory(zipPath, partial);

            // Both apps put the session's folder at the root. An archive packed by hand may not have one, or
            // may carry macOS's __MACOSX next to it; then the whole content is the session.
            var inner = Path.Combine(partial, name);
            bool onlyTheFolder = Directory.Exists(inner) &&
                                 Directory.EnumerateFileSystemEntries(partial).All(e => e == inner || IsMacMetadata(e));
            if (onlyTheFolder)
            {
                Directory.Move(inner, folder);
                Directory.Delete(partial, recursive: true);
            }
            else
            {
                Directory.Move(partial, folder);
            }
        }

        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                   .Where(f => !IsMacMetadata(f) && Path.GetExtension(f).ToLowerInvariant() is ".mov" or ".mp4")
                   .OrderBy(f => f, StringComparer.Ordinal)
                   .FirstOrDefault()
               ?? throw new FileNotFoundException($"{Path.GetFileName(zipPath)} holds no session video.");
    }

    // The resource forks macOS adds when it zips a folder itself: "__MACOSX/…" and "._name" copies.
    private static bool IsMacMetadata(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p == "__MACOSX" || p.StartsWith("._", StringComparison.Ordinal));
}
