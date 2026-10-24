namespace Basin.Kinematics;

/// <summary>
/// Turns frame ticks into elapsed time. A tick that does not advance past the last one gives zero, so a consumer can step it for every output tick.
/// </summary>
public struct TickDelta
{
    /// <summary>
    /// The largest elapsed time that one call to <see cref="Advance"/> returns, in nanoseconds.
    /// </summary>
    public const long MaxStepNanos = 1_000_000_000;

    private long _lastNanos;
    private bool _anchored;

    /// <summary>
    /// Gets a value indicating whether a tick has set the reference time since the last <see cref="Reset"/>.
    /// </summary>
    public readonly bool IsAnchored => _anchored;

    /// <summary>
    /// Gets the time since the last tick that advanced, and makes this tick the reference. The first tick after a reset counts one refresh interval.
    /// </summary>
    /// <param name="tick">The frame tick. Its target present time is the reference.</param>
    /// <returns>The elapsed time in nanoseconds, from 0 to <see cref="MaxStepNanos"/>.</returns>
    public long Advance(in FrameTick tick)
    {
        var now = tick.TargetPresentNanos;
        if (!_anchored)
        {
            _anchored = true;
            _lastNanos = now;
            return Math.Clamp(tick.RefreshIntervalNanos, 0, MaxStepNanos);
        }

        var elapsed = now - _lastNanos;
        if (elapsed <= 0)
        {
            return 0;
        }

        _lastNanos = now;
        return Math.Min(elapsed, MaxStepNanos);
    }

    /// <summary>
    /// Clears the reference time, so the next tick anchors again.
    /// </summary>
    public void Reset() => _anchored = false;
}
