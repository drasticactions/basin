namespace Basin.Shell.Nested;

/// <summary>
/// The edge tiling rules: which edge a drag points at, and the box that a tiled window fills.
/// </summary>
public static class Tiling
{
    /// <summary>
    /// The width in logical pixels of the zone at the left and right edges of the work area that tiles a dragged window.
    /// </summary>
    public const int EdgeZone = 16;

    /// <summary>
    /// Finds the tile edge under the pointer during a move.
    /// </summary>
    /// <param name="pointer">The pointer position in logical layout coordinates.</param>
    /// <param name="output">The output box. A pointer outside it gives <see cref="TileEdge.None"/>.</param>
    /// <param name="workArea">The work area that the edge zones measure from.</param>
    /// <param name="tiling">A value indicating whether left and right tiling is on.</param>
    /// <param name="topTiling">A value indicating whether a pointer at the top of the work area maximizes.</param>
    /// <returns>The edge that the pointer is in, or <see cref="TileEdge.None"/>.</returns>
    public static TileEdge EdgeAt(Point pointer, Box output, Box workArea, bool tiling, bool topTiling)
    {
        if (!output.Contains(pointer))
            return TileEdge.None;
        if (tiling && pointer.X < workArea.X + EdgeZone)
            return TileEdge.Left;
        if (tiling && pointer.X >= workArea.Right - EdgeZone)
            return TileEdge.Right;
        if (topTiling && pointer.Y <= workArea.Y)
            return TileEdge.Top;
        return TileEdge.None;
    }

    /// <summary>
    /// Calculates the frame box of a window tiled against an edge.
    /// The right half gets the extra pixel when the work area width is odd.
    /// </summary>
    /// <param name="edge">The tile edge.</param>
    /// <param name="workArea">The work area to divide.</param>
    /// <returns>The left or right half, or the full work area for <see cref="TileEdge.Top"/> and <see cref="TileEdge.None"/>.</returns>
    public static Box RectFor(TileEdge edge, Box workArea)
    {
        var half = workArea.Width / 2;
        return edge switch
        {
            TileEdge.Left => workArea with { Width = half },
            TileEdge.Right => workArea with { X = workArea.X + half, Width = workArea.Width - half },
            _ => workArea,
        };
    }
}
