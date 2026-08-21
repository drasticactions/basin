using Avalonia;
using Basin.XWayland;

namespace Waylonia;

internal static class HostCursor
{
    private static X11Pointer? _pointer;
    private static bool _tried;

    public static PixelPoint? TryGetPosition()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return null;
        }

        if (!_tried)
        {
            _tried = true;
            _pointer = X11Pointer.TryConnect();
        }

        return _pointer?.TryGetPosition() is { } position
            ? new PixelPoint(position.X, position.Y)
            : null;
    }

    public static void Close()
    {
        _pointer?.Dispose();
        _pointer = null;
        _tried = false;
    }
}
