namespace Basin.Capabilities;

/// <summary>
/// The time that compositor animations read. It uses the time base of <see cref="MonotonicClock"/>, and a test can stop it and move it forward.
/// Times that clients see, input times and loop timers never read it.
/// </summary>
public interface IAnimationClock
{
    /// <summary>
    /// Gets the animation time in nanoseconds. It never decreases.
    /// </summary>
    long Nanos { get; }

    /// <summary>
    /// Gets a value indicating whether an animation asked for a frame after the last <see cref="BeginFrame"/>.
    /// </summary>
    bool IsAnimating { get; }

    /// <summary>
    /// Converts a time on the monotonic clock, for example a predicted vblank, to animation time.
    /// </summary>
    /// <param name="monotonicNanos">The time on the monotonic clock in nanoseconds.</param>
    /// <returns>The animation time in nanoseconds. A stopped clock returns its stopped time.</returns>
    long FromMonotonic(long monotonicNanos);

    /// <summary>
    /// Records that an animation runs and needs one more frame. The record clears at the next <see cref="BeginFrame"/> unless an animation calls this method again.
    /// </summary>
    /// <returns><see langword="true"/> when the caller must schedule another frame. <see langword="false"/> when the clock is stopped, because another frame would show the same time.</returns>
    bool MarkAnimating();

    /// <summary>
    /// Starts a frame. A frame producer calls it once for each output frame, before the animations step.
    /// </summary>
    void BeginFrame();
}
