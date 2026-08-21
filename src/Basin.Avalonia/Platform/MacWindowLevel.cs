using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;

namespace Basin.Avalonia;

[SupportedOSPlatform("macos")]
internal static class MacWindowLevel
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    private const nint BackgroundLevel = -2;
    private const nint BelowLevel = -1;
    private const nint FloatingLevel = 3;
    private const nint OverlayLevel = 4;

    internal static void Apply(IPlatformHandle handle, HostStackingBand band)
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

        SendVoid(window, sel_registerName("setLevel:"), LevelFor(band));
    }

    private static nint LevelFor(HostStackingBand band) => band switch
    {
        HostStackingBand.Background => BackgroundLevel,
        HostStackingBand.Below => BelowLevel,
        HostStackingBand.Overlay => OverlayLevel,
        _ => FloatingLevel,
    };

    [DllImport(LibObjC)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector, nint arg);
}
