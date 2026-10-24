namespace Basin.Host;

/// <summary>
/// Animates the primary outputs before a compositor leaves its last frame lit. <see cref="OutputDriver.RetainAndStop"/> drives it.
/// </summary>
public interface IHandoffOutro
{
    /// <summary>
    /// Gets the length of the animation.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Gets a value indicating whether the animation clock can start. While it is <see langword="false"/>, the driver repaints and calls no <see cref="Step"/>. The budget still applies.
    /// </summary>
    bool Ready => true;

    /// <summary>
    /// Prepares the animation. The driver calls it once for each primary output when the sequence starts.
    /// </summary>
    /// <param name="view">The primary output.</param>
    void Begin(OutputView view);

    /// <summary>
    /// Advances the animation on one output. The driver calls it once per repaint.
    /// </summary>
    /// <param name="view">The output that repaints.</param>
    /// <param name="progress">The progress, in [0, 1].</param>
    void Step(OutputView view, double progress);

    /// <summary>
    /// Completes the animation. The driver calls it once for each primary output when the progress reaches 1.
    /// </summary>
    /// <param name="view">The primary output.</param>
    void End(OutputView view);
}
