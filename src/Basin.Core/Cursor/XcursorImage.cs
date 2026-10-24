namespace Basin;

/// <summary>
/// One cursor frame with premultiplied ARGB8888 pixels in memory order B, G, R, A.
/// </summary>
public sealed class XcursorImage
{
    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// Gets the horizontal hotspot in pixels.
    /// </summary>
    public required int HotspotX { get; init; }

    /// <summary>
    /// Gets the vertical hotspot in pixels.
    /// </summary>
    public required int HotspotY { get; init; }

    /// <summary>
    /// Gets the time in milliseconds to show this frame in an animation.
    /// </summary>
    public required int DelayMs { get; init; }

    /// <summary>
    /// Gets the pixels, tightly packed, 4 bytes per pixel.
    /// </summary>
    public required byte[] Pixels { get; init; }
}
