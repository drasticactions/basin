namespace Basin.Shell.Xdg;

/// <summary>
/// The start of an interactive resize, and the math that turns a pointer position into a window box.
/// </summary>
/// <param name="Edges">The edges that the resize moves.</param>
/// <param name="Start">The window box at the start, in layout coordinates.</param>
/// <param name="GrabX">The horizontal pointer position at the start.</param>
/// <param name="GrabY">The vertical pointer position at the start.</param>
public readonly record struct ResizeDrag(ResizeEdges Edges, Box Start, double GrabX, double GrabY)
{
    /// <summary>
    /// Finds the window box for a pointer position. A resize from the left or the top edge keeps the opposite edge of <see cref="Start"/> in place.
    /// </summary>
    /// <param name="x">The horizontal pointer position.</param>
    /// <param name="y">The vertical pointer position.</param>
    /// <param name="currentX">The left edge to keep when the resize does not move the left edge.</param>
    /// <param name="currentY">The top edge to keep when the resize does not move the top edge.</param>
    /// <param name="minWidth">The smallest width that the result can have.</param>
    /// <param name="minHeight">The smallest height that the result can have.</param>
    /// <returns>The new window box in layout coordinates.</returns>
    public Box BoxFor(double x, double y, int currentX, int currentY, int minWidth = 32, int minHeight = 32)
    {
        var dx = (int)(x - GrabX);
        var dy = (int)(y - GrabY);
        var width = Math.Max(
            Start.Width + ((Edges & ResizeEdges.Left) != ResizeEdges.None ? -dx
                : (Edges & ResizeEdges.Right) != ResizeEdges.None ? dx : 0), minWidth);
        var height = Math.Max(
            Start.Height + ((Edges & ResizeEdges.Top) != ResizeEdges.None ? -dy
                : (Edges & ResizeEdges.Bottom) != ResizeEdges.None ? dy : 0), minHeight);
        var moveX = (Edges & ResizeEdges.Left) != ResizeEdges.None ? Start.Right - width : currentX;
        var moveY = (Edges & ResizeEdges.Top) != ResizeEdges.None ? Start.Bottom - height : currentY;
        return new Box(moveX, moveY, width, height);
    }
}
