namespace Basin.Capabilities;

/// <summary>
/// Tells when the compositor has stopped drawing, so a caller can wait for the screen to be stable.
/// </summary>
public interface IFrameSettle
{
    /// <summary>
    /// Gets a value indicating whether no output has a repaint pending and no UI is due to draw soon.
    /// </summary>
    bool IsSettled { get; }

    /// <summary>
    /// Gets the count of frames rendered on all outputs. It only increases, so a change shows that a frame was drawn.
    /// </summary>
    long Presented { get; }

    /// <summary>
    /// Occurs when the compositor becomes settled while a handler is attached.
    /// </summary>
    event Action? Settled;
}
