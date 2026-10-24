namespace Basin.Capabilities;

/// <summary>
/// The pointer state and the change times that a frame renderer needs to draw hover, press and fade effects without
/// state of its own. Times are in nanoseconds on the clock of the frame, which is the monotonic clock by default.
/// </summary>
/// <param name="Hot">The part under the pointer, or <see cref="FramePart.None"/>.</param>
/// <param name="Pressed">The part that a press latched, or <see cref="FramePart.None"/>.</param>
public readonly record struct FrameInteraction(FramePart Hot, FramePart Pressed)
{
    /// <summary>
    /// Gets the hot part before the last hover change.
    /// </summary>
    public FramePart PreviousHot { get; init; }

    /// <summary>
    /// Gets the pressed part before the last press change.
    /// </summary>
    public FramePart PreviousPressed { get; init; }

    /// <summary>
    /// Gets the time of the last hover change.
    /// </summary>
    public long HoverChangedAt { get; init; }

    /// <summary>
    /// Gets the time of the last press change.
    /// </summary>
    public long PressChangedAt { get; init; }

    /// <summary>
    /// Gets the time when the window last became active or inactive, or 0 when it has not changed.
    /// </summary>
    public long ActiveChangedAt { get; init; }

    /// <summary>
    /// Gets the time of the draw that this value is for.
    /// </summary>
    public long Now { get; init; }

    /// <summary>
    /// Gets the horizontal pointer position in the coordinates of the renderer surface.
    /// </summary>
    public double PointerX { get; init; }

    /// <summary>
    /// Gets the vertical pointer position in the coordinates of the renderer surface.
    /// </summary>
    public double PointerY { get; init; }

    /// <summary>
    /// Gets a value indicating whether the pointer is over the frame. It is false while the pointer is over the client.
    /// </summary>
    public bool PointerInside { get; init; }

    /// <summary>
    /// Gets the time of the last pointer enter, motion or leave.
    /// </summary>
    public long PointerChangedAt { get; init; }
}
