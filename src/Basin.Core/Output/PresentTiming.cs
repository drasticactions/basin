namespace Basin;

/// <summary>
/// Holds the presentation-time arithmetic that fifo and commit-timing share, ported from gamescope. All times are monotonic nanoseconds.
/// </summary>
public static class PresentTiming
{
    /// <summary>
    /// The largest tolerance in nanoseconds that a target time can be past a predicted vblank and still count as reached.
    /// </summary>
    public const long MaxToleranceNanos = 50_000;

    /// <summary>
    /// Gets the tolerance for a refresh interval: one percent of the interval, but never more than <see cref="MaxToleranceNanos"/>.
    /// </summary>
    /// <param name="intervalNanos">The refresh interval in nanoseconds. Zero or a negative value gives <see cref="MaxToleranceNanos"/>.</param>
    /// <returns>The tolerance in nanoseconds.</returns>
    public static long Tolerance(long intervalNanos) =>
        intervalNanos <= 0 ? MaxToleranceNanos : Math.Min(MaxToleranceNanos, intervalNanos / 100);

    /// <summary>
    /// Gets the tolerance for the current refresh interval of an output.
    /// </summary>
    /// <param name="output">The output whose mode sets the interval.</param>
    /// <returns>The tolerance in nanoseconds.</returns>
    public static long Tolerance(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Tolerance(output.CurrentMode.RefreshIntervalNanoseconds);
    }

    /// <summary>
    /// Gets the refresh grid of an output: its refresh interval, or zero while variable refresh is on.
    /// </summary>
    /// <param name="output">The output to read.</param>
    /// <returns>The grid in nanoseconds, or zero for no fixed grid.</returns>
    public static long GridOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.AdaptiveSync ? 0 : output.CurrentMode.RefreshIntervalNanoseconds;
    }

    /// <summary>
    /// Tells whether a frame at the predicted vblank can present an update that names a target time. On a fixed grid with an anchor, it counts refreshes and rounds to the nearest one. Otherwise it compares times with <see cref="Tolerance(long)"/>.
    /// </summary>
    /// <param name="threshold">The target time of the update.</param>
    /// <param name="predicted">The predicted time of the vblank that the frame aims at.</param>
    /// <param name="anchor">The time of the surface's last presentation, or zero for none.</param>
    /// <param name="anchorGrid">The refresh grid at the time of the anchor.</param>
    /// <param name="grid">The refresh grid now, or zero for no fixed grid.</param>
    /// <returns><see langword="true"/> when the frame can present the update.</returns>
    public static bool TargetReached(long threshold, long predicted, long anchor, long anchorGrid, long grid)
    {
        if (anchor <= 0 || grid <= 0 || anchorGrid != grid)
        {
            return threshold <= predicted + (grid > 0 ? Tolerance(grid) : 0);
        }

        if (threshold <= anchor)
        {
            return true;
        }

        var span = threshold - anchor;
        var required = (span / grid) + (span % grid != 0 ? 1 : 0);
        var elapsed = predicted > anchor ? predicted - anchor : 0;
        return (elapsed / grid) + (elapsed % grid >= (grid + 1) / 2 ? 1 : 0) >= required;
    }

    /// <summary>
    /// Predicts the next vblank on a fixed grid. A target in the future is the answer. A stale target moves forward by whole intervals to the first vblank at or after now.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="target">A known vblank time on the grid.</param>
    /// <param name="interval">The refresh interval, or zero or less to return <paramref name="now"/>.</param>
    /// <returns>The predicted vblank time. It saturates at <see cref="long.MaxValue"/>.</returns>
    public static long PredictFixed(long now, long target, long interval)
    {
        if (target >= now)
        {
            return target;
        }

        if (interval <= 0)
        {
            return now;
        }

        var remainder = (now - target) % interval;
        return Add(now, remainder != 0 ? interval - remainder : 0);
    }

    /// <summary>
    /// Predicts the vblank for a latch on a fixed grid. A latch outside the repaint pass that runs after its wakeup misses that vblank and moves to the next one.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="target">The vblank that the repaint aimed at.</param>
    /// <param name="wakeup">The time that the repaint timer woke up.</param>
    /// <param name="interval">The refresh interval.</param>
    /// <param name="inPass"><see langword="true"/> when the latch runs inside the repaint pass.</param>
    /// <returns>The predicted vblank time.</returns>
    public static long PredictFixedLatch(long now, long target, long wakeup, long interval, bool inPass)
    {
        var lead = !inPass && target > wakeup ? target - wakeup : 0;
        return PredictFixed(Add(now, lead), target, interval);
    }

    /// <summary>
    /// Predicts the next flip with variable refresh. It is the later of now plus the render offset and the last flip plus one interval, so a stale vblank does not come back.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="offset">The time that a frame needs from now to the flip.</param>
    /// <param name="last">The time of the last flip.</param>
    /// <param name="interval">The shortest refresh interval of the panel.</param>
    /// <returns>The predicted flip time. It saturates at <see cref="long.MaxValue"/>.</returns>
    public static long Predict(long now, long offset, long last, long interval) =>
        Math.Max(Add(now, offset), Add(last, interval));

    /// <summary>
    /// Gets the time to wake up so that a frame is ready by a target.
    /// </summary>
    /// <param name="target">The target time.</param>
    /// <param name="offset">The time that a frame needs before the target.</param>
    /// <returns>The wake time, or <see langword="null"/> when the target is not later than the offset.</returns>
    public static long? TargetWake(long target, long offset) => target > offset ? target - offset : null;

    private static long Add(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
}
