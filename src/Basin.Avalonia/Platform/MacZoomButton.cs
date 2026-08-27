using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;

namespace Basin.Avalonia;

[SupportedOSPlatform("macos")]
internal static unsafe class MacZoomButton
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    private const nuint StyleMaskFullScreen = 1 << 14;

    private static delegate* unmanaged<nint, nint, nint, void> _original;

    private static bool _installed;

    private static readonly HashSet<nint> FullScreenWindows = [];

    internal static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        var window = objc_getClass("AvnWindow");
        var baseClass = objc_getClass("NSWindow");
        if (window == 0 || baseClass == 0)
        {
            return;
        }

        var selector = sel_registerName("toggleFullScreen:");
        var original = class_getMethodImplementation(baseClass, selector);
        if (original == 0)
        {
            return;
        }

        _original = (delegate* unmanaged<nint, nint, nint, void>)original;
        delegate* unmanaged<nint, nint, nint, void> imp = &ToggleFullScreen;
        if (!class_addMethod(window, selector, (nint)imp, "v@:@"))
        {
            _original = null;
        }
    }

    internal static void UseFullScreen(IPlatformHandle? handle)
    {
        if (handle is IMacOSTopLevelPlatformHandle { NSWindow: var window } && window != 0)
        {
            FullScreenWindows.Add(window);
        }
    }

    internal static void Forget(IPlatformHandle? handle)
    {
        if (handle is IMacOSTopLevelPlatformHandle { NSWindow: var window } && window != 0)
        {
            FullScreenWindows.Remove(window);
        }
    }

    [UnmanagedCallersOnly]
    private static void ToggleFullScreen(nint self, nint selector, nint sender)
    {
        try
        {
            if (ShouldZoom(self, sender))
            {
                SendVoid(self, sel_registerName("performZoom:"), sender);
                return;
            }

            if (_original is not null)
            {
                _original(self, selector, sender);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static bool ShouldZoom(nint self, nint sender)
    {
        if (sender == 0 || FullScreenWindows.Contains(self))
        {
            return false;
        }

        var button = objc_getClass("NSButton");
        if (button == 0 || SendBool(sender, sel_registerName("isKindOfClass:"), button) == 0)
        {
            return false;
        }

        return (SendMask(self, sel_registerName("styleMask")) & StyleMaskFullScreen) == 0;
    }

    [DllImport(LibObjC)]
    private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC)]
    private static extern nint class_getMethodImplementation(nint cls, nint selector);

    [DllImport(LibObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(
        nint cls, nint selector, nint imp, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector, nint arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern sbyte SendBool(nint receiver, nint selector, nint arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint SendMask(nint receiver, nint selector);
}
