namespace Basin.Capabilities;

/// <summary>
/// Stops, moves forward and starts again the <see cref="IAnimationClock"/> of a session. The time of the clock never decreases.
/// </summary>
public interface IAnimationClockControl
{
    /// <summary>
    /// Gets a value indicating whether the clock is stopped.
    /// </summary>
    bool IsFrozen { get; }

    /// <summary>
    /// Stops the clock at its current time. An animation that starts while the clock is stopped counts from this time. A stopped clock ignores the call.
    /// </summary>
    void Freeze();

    /// <summary>
    /// Moves the time forward and asks for a frame. A running clock keeps the jump.
    /// </summary>
    /// <param name="nanos">The step in nanoseconds.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nanos"/> is negative.</exception>
    void Advance(long nanos);

    /// <summary>
    /// Starts the clock again from its stopped time and asks for a frame. A running clock ignores the call.
    /// </summary>
    void Resume();
}
