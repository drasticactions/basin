using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace Basin.Avalonia;

[SupportedOSPlatform("macos")]
internal static class MacFrameCursor
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    private const int PositionTop = 1 << 0;
    private const int PositionLeft = 1 << 1;
    private const int PositionBottom = 1 << 2;
    private const int PositionRight = 1 << 3;
    private const int DirectionsAll = 3;
    private const int OperationCopy = 1;
    private const int FileTypePng = 4;

    internal static Cursor? For(StandardCursorType type)
    {
        var position = type switch
        {
            StandardCursorType.TopSide => PositionTop,
            StandardCursorType.LeftSide => PositionLeft,
            StandardCursorType.BottomSide => PositionBottom,
            StandardCursorType.RightSide => PositionRight,
            StandardCursorType.TopLeftCorner => PositionTop | PositionLeft,
            StandardCursorType.TopRightCorner => PositionTop | PositionRight,
            StandardCursorType.BottomLeftCorner => PositionBottom | PositionLeft,
            StandardCursorType.BottomRightCorner => PositionBottom | PositionRight,
            _ => 0,
        };
        return position == 0 ? null : Create(position);
    }

    private static Cursor? Create(int position)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var cursorClass = objc_getClass("NSCursor");
            var frameResize = sel_registerName("frameResizeCursorFromPosition:inDirections:");
            if (cursorClass == 0 ||
                SendBool(cursorClass, sel_registerName("respondsToSelector:"), frameResize) == 0)
            {
                return null;
            }

            var cursor = Send(cursorClass, frameResize, position, DirectionsAll);
            if (cursor == 0)
            {
                return null;
            }

            var image = Send(cursor, sel_registerName("image"));
            if (image == 0)
            {
                return null;
            }

            var hot = SendPoint(cursor, sel_registerName("hotSpot"));
            var size = SendSize(image, sel_registerName("size"));
            var width = (nint)Math.Ceiling(size.Width);
            var height = (nint)Math.Ceiling(size.Height);
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var colorSpace = SendUtf8(
                objc_getClass("NSString"),
                sel_registerName("stringWithUTF8String:"),
                "NSCalibratedRGBColorSpace");
            var rep = SendInitBitmap(
                Send(objc_getClass("NSBitmapImageRep"), sel_registerName("alloc")),
                sel_registerName(
                    "initWithBitmapDataPlanes:pixelsWide:pixelsHigh:bitsPerSample:samplesPerPixel:" +
                    "hasAlpha:isPlanar:colorSpaceName:bytesPerRow:bitsPerPixel:"),
                0, width, height, 8, 4, 1, 0, colorSpace, 0, 0);
            if (rep == 0)
            {
                return null;
            }

            try
            {
                var graphicsClass = objc_getClass("NSGraphicsContext");
                var context = Send(graphicsClass, sel_registerName("graphicsContextWithBitmapImageRep:"), rep);
                if (context == 0)
                {
                    return null;
                }

                SendVoid(graphicsClass, sel_registerName("saveGraphicsState"));
                SendVoid(graphicsClass, sel_registerName("setCurrentContext:"), context);
                SendDraw(
                    image,
                    sel_registerName("drawInRect:fromRect:operation:fraction:"),
                    new NsRect { Width = size.Width, Height = size.Height },
                    default,
                    OperationCopy,
                    1.0);
                SendVoid(graphicsClass, sel_registerName("restoreGraphicsState"));

                var png = Send(
                    rep,
                    sel_registerName("representationUsingType:properties:"),
                    FileTypePng,
                    Send(objc_getClass("NSDictionary"), sel_registerName("dictionary")));
                if (png == 0)
                {
                    return null;
                }

                var length = (int)Send(png, sel_registerName("length"));
                var bytes = Send(png, sel_registerName("bytes"));
                if (length <= 0 || bytes == 0)
                {
                    return null;
                }

                var data = new byte[length];
                Marshal.Copy(bytes, data, 0, length);
                using var stream = new MemoryStream(data);
                return new Cursor(new Bitmap(stream), new PixelPoint((int)hot.X, (int)hot.Y));
            }
            finally
            {
                SendVoid(rep, sel_registerName("release"));
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NsPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NsSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NsRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [DllImport(LibObjC)]
    private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC)]
    private static extern nint objc_autoreleasePoolPush();

    [DllImport(LibObjC)]
    private static extern void objc_autoreleasePoolPop(nint pool);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint arg, nint arg2);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector, nint arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern sbyte SendBool(nint receiver, nint selector, nint arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern NsPoint SendPoint(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern NsSize SendSize(nint receiver, nint selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendUtf8(
        nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendDraw(
        nint receiver, nint selector, NsRect inRect, NsRect fromRect, nint operation, double fraction);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendInitBitmap(
        nint receiver,
        nint selector,
        nint planes,
        nint pixelsWide,
        nint pixelsHigh,
        nint bitsPerSample,
        nint samplesPerPixel,
        nint hasAlpha,
        nint isPlanar,
        nint colorSpaceName,
        nint bytesPerRow,
        nint bitsPerPixel);
}
