namespace Basin.Host;

/// <summary>
/// Animates the frame that a compositor inherits from the previous one into its own scene. Each method gets the <see cref="HandoffFrame"/> of one output.
/// </summary>
public interface IHandoffTransition
{
    /// <summary>
    /// Gets the length of the transition.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Sets the start state. The driver calls it once when the transition is created.
    /// </summary>
    /// <param name="frame">The frame of one output.</param>
    void Begin(HandoffFrame frame);

    /// <summary>
    /// Advances the transition. The driver calls it from the repaint, before <see cref="OutputDriver.BeforeRepaint"/>. It must not allocate.
    /// </summary>
    /// <param name="frame">The frame of one output.</param>
    /// <param name="progress">The progress, in [0, 1).</param>
    void Step(HandoffFrame frame, double progress);

    /// <summary>
    /// Sets the end state. The driver calls it once, and then removes the frame's node and releases the frame.
    /// </summary>
    /// <param name="frame">The frame of one output.</param>
    void End(HandoffFrame frame);
}
