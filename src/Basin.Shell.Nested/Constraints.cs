using Basin.Shell.Xdg;

namespace Basin.Shell.Nested;

/// <summary>
/// The rules that keep a window frame reachable on the work area. All boxes are in logical layout coordinates.
/// A frame can go off the side by its extent minus a quarter of it, clamped from <see cref="MinimumOnScreen"/> to <see cref="MaximumOnScreen"/>.
/// </summary>
public static class Constraints
{
    /// <summary>
    /// The smallest part of a frame extent, in logical pixels, that must stay on the work area.
    /// </summary>
    public const int MinimumOnScreen = 10;

    /// <summary>
    /// The largest part of a frame extent, in logical pixels, that the rules require on the work area.
    /// </summary>
    public const int MaximumOnScreen = 75;

    /// <summary>
    /// Moves a frame so that its title bar stays fully inside the work area on the vertical axis.
    /// On the horizontal axis the frame can go partly off each side.
    /// </summary>
    /// <param name="frame">The frame box to constrain.</param>
    /// <param name="workArea">The work area.</param>
    /// <param name="titleHeight">The title bar height in logical pixels.</param>
    /// <returns>The frame at the constrained position, with its size unchanged.</returns>
    public static Box KeepTitleOnScreen(Box frame, Box workArea, int titleHeight)
    {
        var offscreen = AllowedOffscreen(frame.Width);
        var x = Shove(frame.X, frame.Width, workArea.X - offscreen, workArea.Right + offscreen);
        var y = Shove(frame.Y, frame.Height, workArea.Y, workArea.Bottom + frame.Height - titleHeight);
        return frame with { X = x, Y = y };
    }

    /// <summary>
    /// Moves a frame so that part of it stays on the work area. It can go partly off the left, the right and the top.
    /// The title bar stays above the bottom of the work area.
    /// </summary>
    /// <param name="frame">The frame box to constrain.</param>
    /// <param name="workArea">The work area.</param>
    /// <param name="titleHeight">The title bar height in logical pixels.</param>
    /// <returns>The frame at the constrained position, with its size unchanged.</returns>
    public static Box PartiallyOnScreen(Box frame, Box workArea, int titleHeight)
    {
        var offscreenX = AllowedOffscreen(frame.Width);
        var offscreenY = AllowedOffscreen(frame.Height);
        var x = Shove(frame.X, frame.Width, workArea.X - offscreenX, workArea.Right + offscreenX);
        var y = Shove(frame.Y, frame.Height, workArea.Y - offscreenY, workArea.Bottom + frame.Height - titleHeight);
        return frame with { X = x, Y = y };
    }

    /// <summary>
    /// Clips the dragged edges of a frame during a resize. The top and bottom edges stop at the work area.
    /// The left and right edges can go off by the allowed amount. The frame never gets smaller than the minimum size.
    /// </summary>
    /// <param name="frame">The frame box that the drag asks for.</param>
    /// <param name="edges">The edges that the drag moves. The other edges are not clipped.</param>
    /// <param name="workArea">The work area.</param>
    /// <param name="minWidth">The smallest frame width in logical pixels.</param>
    /// <param name="minHeight">The smallest frame height in logical pixels.</param>
    /// <returns>The clipped frame box.</returns>
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

    /// <summary>
    /// Shrinks a frame to the work area size when it is larger, then moves it fully inside the work area.
    /// </summary>
    /// <param name="frame">The frame box to fit.</param>
    /// <param name="workArea">The work area.</param>
    /// <returns>A frame box inside the work area.</returns>
    public static Box FitWorkArea(Box frame, Box workArea)
    {
        var width = Math.Min(frame.Width, workArea.Width);
        var height = Math.Min(frame.Height, workArea.Height);
        var x = Shove(frame.X, width, workArea.X, workArea.Right);
        var y = Shove(frame.Y, height, workArea.Y, workArea.Bottom);
        return new Box(x, y, width, height);
    }

    /// <summary>
    /// Clamps a size to the limits that a client sets. A maximum of zero or less means no maximum.
    /// A maximum below the minimum counts as the minimum.
    /// </summary>
    /// <param name="width">The width to clamp.</param>
    /// <param name="height">The height to clamp.</param>
    /// <param name="minWidth">The smallest width.</param>
    /// <param name="minHeight">The smallest height.</param>
    /// <param name="maxWidth">The largest width, or zero for no limit.</param>
    /// <param name="maxHeight">The largest height, or zero for no limit.</param>
    /// <returns>The clamped width and height.</returns>
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
