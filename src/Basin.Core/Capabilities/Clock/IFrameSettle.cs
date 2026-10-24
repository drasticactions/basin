namespace Basin.Capabilities;

/// <summary>
/// Tells when the compositor has stopped drawing, so a caller can wait for the screen to be stable.
/// </summary>
public interface IFrameSettle
{
    /// <summary>
    /// Gets a value indicating whether no output has a repaint pending, no UI is due to draw soon, every mapped window answered its newest configure and no animation asked for another frame.
    /// A window answers a configure when it acknowledges it, and commits after it when the configure asked for a new size.
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

    /// <summary>
    /// Describes what keeps the compositor unsettled. The description names each window that did not answer its newest configure by app id and title, each output with a repaint pending, and a running animation.
    /// </summary>
    /// <returns>One sentence for each reason, or an empty string when the compositor is settled.</returns>
    string DescribeUnsettled();
}
