using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace Basin.Avalonia;

[SupportedOSPlatform("macos")]
internal static class MacPointerLocation
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint CombinedSessionState = 0;

    internal static PixelPoint? TryGet()
    {
        var source = CGEventSourceCreate(CombinedSessionState);
        var locationEvent = CGEventCreate(source);
        if (source != IntPtr.Zero)
        {
            CFRelease(source);
        }

        if (locationEvent == IntPtr.Zero)
        {
            return null;
        }

        var location = CGEventGetLocation(locationEvent);
        CFRelease(locationEvent);
        return new PixelPoint((int)Math.Round(location.X), (int)Math.Round(location.Y));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;

        public double Y;
    }

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventSourceCreate(uint stateId);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics)]
    private static extern CGPoint CGEventGetLocation(IntPtr locationEvent);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr reference);
}
