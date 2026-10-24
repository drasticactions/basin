namespace Basin;

/// <summary>
/// One cursor from an Xcursor file, at one nominal size. It has one frame, or more frames for an animation.
/// </summary>
public sealed class XcursorCursor
{
    /// <summary>
    /// Gets the frames in display order. The list is not empty.
    /// </summary>
    public required IReadOnlyList<XcursorImage> Frames { get; init; }

    /// <summary>
    /// Gets a frame. The index wraps around the frame count, so that an animation can loop.
    /// </summary>
    /// <param name="index">The frame index. It must not be negative.</param>
    /// <returns>The frame at the wrapped index.</returns>
    public XcursorImage Frame(int index) => Frames[index % Frames.Count];
}
