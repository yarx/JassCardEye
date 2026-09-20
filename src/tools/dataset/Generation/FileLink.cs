using System.Runtime.InteropServices;

namespace JassCardEye.Dataset.Generation;

/// <summary>
/// Shares files between the dataset folders without duplicating them on disk.
///
/// Symbolic links cannot be used here: Ultralytics resolves them before deriving the label path from
/// the image path, so an image reached through <c>detect/images -&gt; ../images</c> would be looked up
/// in <c>&lt;root&gt;/labels/</c> instead of <c>&lt;root&gt;/detect/labels/</c> and every frame would
/// silently count as background. A class folder would lose its class name the same way. Hard links do
/// not have that problem - they are ordinary directory entries pointing at the same data - and unlike
/// symlinks they need no Developer Mode on Windows. Where hard links are unavailable (a different
/// volume, an exotic file system) the file is copied instead, so the result is always complete.
/// </summary>
public static class FileLink
{
    /// <summary>Links <paramref name="linkPath"/> to the same data as <paramref name="sourcePath"/>, or copies it.</summary>
    public static void LinkFile(string linkPath, string sourcePath)
    {
        Remove(linkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        if (!TryCreateHardLink(linkPath, sourcePath))
            File.Copy(sourcePath, linkPath, overwrite: true);
    }

    /// <summary>
    /// Recreates <paramref name="destinationDir"/> as a real directory whose files are hard links to
    /// those in <paramref name="sourceDir"/> (recursively).
    /// </summary>
    public static void MirrorDirectory(string destinationDir, string sourceDir)
    {
        Remove(destinationDir);
        Mirror(destinationDir, sourceDir);
    }

    private static void Mirror(string destinationDir, string sourceDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            LinkFile(Path.Combine(destinationDir, Path.GetFileName(file)), file);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            Mirror(Path.Combine(destinationDir, Path.GetFileName(dir)), dir);
    }

    /// <summary>Removes a file, directory or link, whichever is present.</summary>
    public static void Remove(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                // A directory symlink must be unlinked, not emptied – never touch its target.
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    Directory.Delete(path);
                else
                    Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (DirectoryNotFoundException) { /* nothing to remove */ }
        catch (FileNotFoundException) { /* nothing to remove */ }
    }

    private static bool TryCreateHardLink(string linkPath, string sourcePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return CreateHardLinkW(linkPath, sourcePath, IntPtr.Zero);
            return link(sourcePath, linkPath) == 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);
}
