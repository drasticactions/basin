namespace Basin;

public sealed class XcursorImage
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int HotspotX { get; init; }

    public required int HotspotY { get; init; }

    public required int DelayMs { get; init; }

    public required byte[] Pixels { get; init; }
}
