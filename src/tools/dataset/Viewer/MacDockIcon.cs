using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace JassCardEye.Dataset.Viewer;

/// <summary>
/// Puts the app icon into the macOS Dock.
///
/// A Mac app normally gets its Dock icon from its bundle. Started with <c>dotnet run</c> the viewer has
/// none, and Avalonia does not pass the window icon on to the Dock, so macOS shows a blank document.
/// Setting the icon on the running application through Cocoa is what a bundle would otherwise do.
/// </summary>
internal static class MacDockIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    /// <summary>Sets the Dock icon from a PNG resource. Does nothing on other platforms.</summary>
    public static void Apply(Uri png)
    {
        if (!OperatingSystem.IsMacOS()) return;

        byte[] bytes;
        using (var stream = AssetLoader.Open(png))
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            bytes = memory.ToArray();
        }

        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            // [[NSData alloc] initWithBytes:length:] copies the bytes, so the pin only has to last this call.
            var data = Send(Send(objc_getClass("NSData"), sel_registerName("alloc")),
                            sel_registerName("initWithBytes:length:"), pinned.AddrOfPinnedObject(), (nuint)bytes.Length);
            var image = Send(Send(objc_getClass("NSImage"), sel_registerName("alloc")), sel_registerName("initWithData:"), data);
            if (image == IntPtr.Zero) return;

            var application = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            Send(application, sel_registerName("setApplicationIconImage:"), image);
        }
        finally
        {
            pinned.Free();
        }
    }
}
