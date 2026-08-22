using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Basin.Avalonia;

namespace Waylonia;

[SupportedOSPlatform("macos")]
internal static class MacScreenScales
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public static double? TryGetScale(HostScreenInfo info)
    {
        Span<uint> displays = stackalloc uint[16];
        uint count;
        unsafe
        {
            fixed (uint* list = displays)
            {
                if (CGGetActiveDisplayList((uint)displays.Length, list, &count) != 0)
                {
                    return null;
                }
            }
        }

        for (var i = 0; i < (int)count; i++)
        {
            var bounds = CGDisplayBounds(displays[i]);
            if ((int)Math.Round(bounds.X) != info.X || (int)Math.Round(bounds.Y) != info.Y)
            {
                continue;
            }

            var mode = CGDisplayCopyDisplayMode(displays[i]);
            if (mode == IntPtr.Zero)
            {
                return null;
            }

            var points = CGDisplayModeGetWidth(mode);
            var pixels = CGDisplayModeGetPixelWidth(mode);
            CGDisplayModeRelease(mode);
            if (points == 0)
            {
                return null;
            }

            return (double)pixels / points;
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [DllImport(CoreGraphics)]
    private static extern unsafe int CGGetActiveDisplayList(uint maxDisplays, uint* activeDisplays, uint* displayCount);

    [DllImport(CoreGraphics)]
    private static extern CGRect CGDisplayBounds(uint display);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGDisplayCopyDisplayMode(uint display);

    [DllImport(CoreGraphics)]
    private static extern nuint CGDisplayModeGetWidth(IntPtr mode);

    [DllImport(CoreGraphics)]
    private static extern nuint CGDisplayModeGetPixelWidth(IntPtr mode);

    [DllImport(CoreGraphics)]
    private static extern void CGDisplayModeRelease(IntPtr mode);
}
