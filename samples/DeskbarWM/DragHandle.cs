using Basin.WindowManager;

namespace DeskbarWm;

internal static class DragHandle
{
    public const int Thickness = 7;

    public static Rect For(bool horizontal, Size size) => horizontal
        ? new Rect(size.Width - Thickness, 0, Thickness, size.Height)
        : new Rect(0, size.Height - Thickness, size.Width, Thickness);
}
