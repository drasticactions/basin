using System.Runtime.InteropServices;

namespace Basin.Sdl;

internal static unsafe class SdlObjC
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string MetalFramework = "/System/Library/Frameworks/Metal.framework/Metal";

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width;
        public double Height;
    }

    public static nint Selector(string name)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            return sel_registerName((byte*)utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [DllImport(Runtime)]
    private static extern nint sel_registerName(byte* name);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    public static extern nint Send(nint receiver, nint selector);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    public static extern nint Send(nint receiver, nint selector, nint argument);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    public static extern void SendULong(nint receiver, nint selector, ulong argument);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    public static extern void SendBool(nint receiver, nint selector, byte argument);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    public static extern void SendSize(nint receiver, nint selector, CGSize argument);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    public static extern void SendDouble(nint receiver, nint selector, double argument);

    [DllImport(Runtime, EntryPoint = "objc_msgSend")]
    public static extern double SendReturnDouble(nint receiver, nint selector);

    [DllImport(Runtime)]
    public static extern nint objc_autoreleasePoolPush();

    [DllImport(Runtime)]
    public static extern void objc_autoreleasePoolPop(nint pool);

    [DllImport(MetalFramework)]
    public static extern nint MTLCreateSystemDefaultDevice();

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(CoreGraphics)]
    public static extern nint CGColorSpaceCreateWithName(nint name);

    [DllImport(CoreGraphics)]
    public static extern void CGColorSpaceRelease(nint space);

    public static nint ColorSpaceNamed(string symbol)
    {
        var library = NativeLibrary.Load(CoreGraphics);
        return NativeLibrary.TryGetExport(library, symbol, out var address) ? CGColorSpaceCreateWithName(*(nint*)address) : 0;
    }
}
