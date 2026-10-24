namespace Basin.Shell.Xdg;

/// <summary>
/// The hit test for a resize band around the left, right and bottom edges of a window frame. The top edge has no band.
/// </summary>
public static class ResizeRing
{
    /// <summary>
    /// Finds the resize edges at a point near a frame.
    /// </summary>
    /// <param name="frame">The window frame.</param>
    /// <param name="x">The horizontal position of the point.</param>
    /// <param name="y">The vertical position of the point.</param>
    /// <param name="margin">The width of the band outside the frame.</param>
    /// <param name="corner">The length from a bottom corner, along each edge, that counts as the corner.</param>
    /// <returns>The edges at the point, or <see cref="ResizeEdges.None"/> when the point is inside the frame, above its top edge, or outside the band.</returns>
    public static ResizeEdges EdgesAt(Box frame, double x, double y, int margin, int corner)
    {
        if (frame.IsEmpty ||
            x < frame.X - margin || x > frame.Right + margin ||
            y < frame.Y || y > frame.Bottom + margin)
        {
            return ResizeEdges.None;
        }

        var edges = ResizeEdges.None;
        if (x < frame.X)
        {
            edges |= ResizeEdges.Left;
        }
        else if (x > frame.Right)
        {
            edges |= ResizeEdges.Right;
        }

        if (y > frame.Bottom)
        {
            edges |= ResizeEdges.Bottom;
            if (edges == ResizeEdges.Bottom)
            {
                if (x < frame.X + corner)
                {
                    edges |= ResizeEdges.Left;
                }
                else if (x > frame.Right - corner)
                {
                    edges |= ResizeEdges.Right;
                }
            }
        }
        else if (edges != ResizeEdges.None && y > frame.Bottom - corner)
        {
            edges |= ResizeEdges.Bottom;
        }

        return edges;
    }

    /// <summary>
    /// Finds the cursor name for resize edges.
    /// </summary>
    /// <param name="edges">The edges that <see cref="EdgesAt"/> returned.</param>
    /// <returns>The xcursor name for a side or a bottom corner, or <c>left_ptr</c> for any other value.</returns>
    public static string CursorFor(ResizeEdges edges) => edges switch
    {
        ResizeEdges.Left => "left_side",
        ResizeEdges.Right => "right_side",
        ResizeEdges.Bottom => "bottom_side",
        ResizeEdges.BottomLeft => "bottom_left_corner",
        ResizeEdges.BottomRight => "bottom_right_corner",
        _ => "left_ptr",
    };
}
