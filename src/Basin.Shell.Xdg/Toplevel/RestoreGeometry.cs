namespace Basin.Shell.Xdg;

/// <summary>
/// The frame that a window returns to when it leaves the maximized or fullscreen state. It keeps the first frame that it saves.
/// </summary>
public readonly record struct RestoreGeometry
{
    /// <summary>
    /// Gets a value with no saved frame.
    /// </summary>
    public static RestoreGeometry None => default;

    /// <summary>
    /// Gets a value indicating whether a frame is saved.
    /// </summary>
    public bool HasValue { get; private init; }

    /// <summary>
    /// Gets the saved frame. It is empty when <see cref="HasValue"/> is false.
    /// </summary>
    public Box Frame { get; private init; }

    /// <summary>
    /// Saves a frame when no frame is saved yet.
    /// </summary>
    /// <param name="frame">The frame to save. An empty frame is not saved.</param>
    /// <returns>A value with the frame, or this value when a frame is saved already or <paramref name="frame"/> is empty.</returns>
    public RestoreGeometry Saving(in Box frame) =>
        HasValue || frame.IsEmpty ? this : new RestoreGeometry { HasValue = true, Frame = frame };

    /// <summary>
    /// Gets the saved frame.
    /// </summary>
    /// <param name="frame">The saved frame, or an empty box when none is saved.</param>
    /// <returns><see langword="true"/> when a frame is saved.</returns>
    public bool TryGet(out Box frame)
    {
        frame = Frame;
        return HasValue;
    }
}
