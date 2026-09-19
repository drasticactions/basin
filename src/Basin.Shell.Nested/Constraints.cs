using Basin.Shell.Xdg;

namespace Basin.Shell.Nested;

public static class Constraints
{
    public const int MinimumOnScreen = 10;

    public const int MaximumOnScreen = 75;

    public static Box KeepTitleOnScreen(Box frame, Box workArea, int titleHeight)
    {
        var offscreen = AllowedOffscreen(frame.Width);
        var x = Shove(frame.X, frame.Width, workArea.X - offscreen, workArea.Right + offscreen);
        var y = Shove(frame.Y, frame.Height, workArea.Y, workArea.Bottom + frame.Height - titleHeight);
        return frame with { X = x, Y = y };
    }

    public static Box PartiallyOnScreen(Box frame, Box workArea, int titleHeight)
    {
        var offscreenX = AllowedOffscreen(frame.Width);
        var offscreenY = AllowedOffscreen(frame.Height);
        var x = Shove(frame.X, frame.Width, workArea.X - offscreenX, workArea.Right + offscreenX);
        var y = Shove(frame.Y, frame.Height, workArea.Y - offscreenY, workArea.Bottom + frame.Height - titleHeight);
        return frame with { X = x, Y = y };
    }

    public static Box ClipResize(Box frame, ResizeEdges edges, Box workArea, int minWidth, int minHeight)
    {
        var offscreen = AllowedOffscreen(frame.Width);
        var x = frame.X;
        var y = frame.Y;
        var width = frame.Width;
        var height = frame.Height;
        if ((edges & ResizeEdges.Left) != ResizeEdges.None && x < workArea.X - offscreen)
        {
            width = Math.Max(minWidth, frame.Right - (workArea.X - offscreen));
            x = frame.Right - width;
        }

        if ((edges & ResizeEdges.Right) != ResizeEdges.None && frame.Right > workArea.Right + offscreen)
        {
            width = Math.Max(minWidth, workArea.Right + offscreen - x);
        }

        if ((edges & ResizeEdges.Top) != ResizeEdges.None && y < workArea.Y)
        {
            height = Math.Max(minHeight, frame.Bottom - workArea.Y);
            y = frame.Bottom - height;
        }

        if ((edges & ResizeEdges.Bottom) != ResizeEdges.None && frame.Bottom > workArea.Bottom)
        {
            height = Math.Max(minHeight, workArea.Bottom - y);
        }

        return new Box(x, y, width, height);
    }

    public static Box FitWorkArea(Box frame, Box workArea)
    {
        var width = Math.Min(frame.Width, workArea.Width);
        var height = Math.Min(frame.Height, workArea.Height);
        var x = Shove(frame.X, width, workArea.X, workArea.Right);
        var y = Shove(frame.Y, height, workArea.Y, workArea.Bottom);
        return new Box(x, y, width, height);
    }

    public static (int Width, int Height) ClampSize(int width, int height, int minWidth, int minHeight, int maxWidth, int maxHeight) =>
        (ClampExtent(width, minWidth, maxWidth), ClampExtent(height, minHeight, maxHeight));

    private static int ClampExtent(int extent, int min, int max)
    {
        var clamped = Math.Max(extent, min);
        return max > 0 ? Math.Min(clamped, Math.Max(max, min)) : clamped;
    }

    private static int AllowedOffscreen(int extent) =>
        Math.Max(0, extent - Math.Clamp(extent / 4, MinimumOnScreen, MaximumOnScreen));

    private static int Shove(int position, int extent, int start, int end)
    {
        if (position < start)
            position = start;
        if (position + extent > end)
            position = end - extent;
        return position;
    }
}
