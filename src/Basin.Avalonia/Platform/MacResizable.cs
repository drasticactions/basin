using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;

namespace Basin.Avalonia;

[SupportedOSPlatform("macos")]
internal static class MacResizable
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    private const nuint StyleMaskResizable = 1 << 3;

    internal static void Apply(IPlatformHandle? handle, bool resizable)
    {
        if (handle is not IMacOSTopLevelPlatformHandle mac)
        {
            return;
        }

        var window = mac.NSWindow;
        if (window == 0)
        {
            return;
        }

        var mask = SendMask(window, sel_registerName("styleMask"));
        var wanted = resizable ? mask | StyleMaskResizable : mask & ~StyleMaskResizable;
        if (wanted != mask)
        {
            SendVoidMask(window, sel_registerName("setStyleMask:"), wanted);
        }
    }

    [DllImport(LibObjC)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint SendMask(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidMask(nint receiver, nint selector, nuint arg);
}
