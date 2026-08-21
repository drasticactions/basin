using System.Runtime.InteropServices;
using Avalonia;

namespace Waylonia;

internal static class HostCursor
{
    public static PixelPoint? TryGetPosition() =>
        GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : null;

    public static void Close()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;

        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
}
