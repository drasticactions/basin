using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Basin.Avalonia;

[SupportedOSPlatform("macos")]
internal static class MacFullscreenSize
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    private static bool _installed;

    internal static unsafe void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        var window = objc_getClass("AvnWindow");
        if (window == 0)
        {
            return;
        }

        var selector = sel_registerName("window:willUseFullScreenContentSize:");
        if (class_getInstanceMethod(window, selector) != 0)
        {
            return;
        }

        delegate* unmanaged<nint, nint, nint, NsSize, NsSize> imp = &WillUseFullScreenContentSize;
        _ = class_addMethod(window, selector, (nint)imp, "{CGSize=dd}@:@{CGSize=dd}");
    }

    [UnmanagedCallersOnly]
    private static NsSize WillUseFullScreenContentSize(nint self, nint selector, nint window, NsSize proposed) =>
        proposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct NsSize
    {
        public double Width;
        public double Height;
    }

    [DllImport(LibObjC)]
    private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC)]
    private static extern nint class_getInstanceMethod(nint cls, nint selector);

    [DllImport(LibObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(
        nint cls, nint selector, nint imp, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);
}
