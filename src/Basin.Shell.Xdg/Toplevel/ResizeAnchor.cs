namespace Basin.Shell.Xdg;

public readonly record struct ResizeAnchor(ResizeEdges Edges, int Right, int Bottom)
{
    public static ResizeAnchor? For(ResizeEdges edges, int x, int y, int width, int height) =>
        (edges & (ResizeEdges.Left | ResizeEdges.Top)) == ResizeEdges.None
            ? null
            : new ResizeAnchor(edges, x + width, y + height);

    public (int X, int Y) PositionFor(int width, int height, int x, int y) => (
        (Edges & ResizeEdges.Left) != ResizeEdges.None ? Right - width : x,
        (Edges & ResizeEdges.Top) != ResizeEdges.None ? Bottom - height : y);

    public static ResizeAnchor? AfterCommit(ResizeAnchor? anchor, bool resizing) =>
        resizing ? anchor : null;
}
