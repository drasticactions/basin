namespace Basin.Shell.Xdg;

/// <summary>
/// Keeps the right or bottom edge of a window in place while a resize from the left or top edge changes its size.
/// </summary>
/// <param name="Edges">The edges of the resize.</param>
/// <param name="Right">The right edge to keep, in layout coordinates.</param>
/// <param name="Bottom">The bottom edge to keep, in layout coordinates.</param>
public readonly record struct ResizeAnchor(ResizeEdges Edges, int Right, int Bottom)
{
    /// <summary>
    /// Makes an anchor for a resize when it moves the left or the top edge.
    /// </summary>
    /// <param name="edges">The edges of the resize.</param>
    /// <param name="x">The left edge of the window at the start.</param>
    /// <param name="y">The top edge of the window at the start.</param>
    /// <param name="width">The width of the window at the start.</param>
    /// <param name="height">The height of the window at the start.</param>
    /// <returns>The anchor, or <see langword="null"/> when <paramref name="edges"/> has neither the left nor the top edge.</returns>
    public static ResizeAnchor? For(ResizeEdges edges, int x, int y, int width, int height) =>
        (edges & (ResizeEdges.Left | ResizeEdges.Top)) == ResizeEdges.None
            ? null
            : new ResizeAnchor(edges, x + width, y + height);

    /// <summary>
    /// Finds the window position for a new size, so that the anchored edges stay in place.
    /// </summary>
    /// <param name="width">The new width.</param>
    /// <param name="height">The new height.</param>
    /// <param name="x">The current left edge, kept when the resize does not move the left edge.</param>
    /// <param name="y">The current top edge, kept when the resize does not move the top edge.</param>
    /// <returns>The new position in layout coordinates.</returns>
    public (int X, int Y) PositionFor(int width, int height, int x, int y) => (
        (Edges & ResizeEdges.Left) != ResizeEdges.None ? Right - width : x,
        (Edges & ResizeEdges.Top) != ResizeEdges.None ? Bottom - height : y);

    /// <summary>
    /// Finds the anchor to keep after a commit.
    /// </summary>
    /// <param name="anchor">The anchor before the commit.</param>
    /// <param name="resizing">A value indicating whether the resize continues.</param>
    /// <returns><paramref name="anchor"/> while the resize continues, and <see langword="null"/> after it.</returns>
    public static ResizeAnchor? AfterCommit(ResizeAnchor? anchor, bool resizing) =>
        resizing ? anchor : null;
}
