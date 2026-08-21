namespace Basin;

public sealed class XcursorCursor
{
    public required IReadOnlyList<XcursorImage> Frames { get; init; }

    public XcursorImage Frame(int index) => Frames[index % Frames.Count];
}
