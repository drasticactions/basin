namespace Basin.Desktop;

/// <summary>
/// Computes when a frame-rate cap lets a surface take its next frame callback, on fixed-rate and on variable-rate outputs.
/// </summary>
public static class FrameLimitCadence
{
    /// <summary>
    /// The time in nanoseconds that a variable-rate cycle can open before its schedule.
    /// </summary>
    public const long EarlyAllowanceNanos = 200_000;

    /// <summary>
    /// Computes how many vblanks one capped frame spans on a fixed-rate output. A cap above the refresh rate gives 1.
    /// </summary>
    /// <param name="refreshMilliHz">The output refresh rate in millihertz.</param>
    /// <param name="fps">The cap in frames per second.</param>
    /// <returns>The vblank divisor. It is 1 when either argument is 0 or less.</returns>
    public static int Divisor(int refreshMilliHz, int fps)
    {
        if (fps <= 0 || refreshMilliHz <= 0)
        {
            return 1;
        }

        var refreshHz = (int)Math.Round(refreshMilliHz / 1000.0);
        return Math.Max(1, refreshHz / Math.Min(fps, refreshHz));
    }

    /// <summary>
    /// Computes the length of one capped frame cycle.
    /// </summary>
    /// <param name="refreshIntervalNanos">The output refresh interval in nanoseconds.</param>
    /// <param name="refreshMilliHz">The output refresh rate in millihertz.</param>
    /// <param name="fps">The cap in frames per second, or 0 for no cap.</param>
    /// <param name="variable">Whether the output uses adaptive sync.</param>
    /// <returns>The cycle in nanoseconds. A variable-rate cycle is never shorter than the refresh interval.</returns>
    public static long LimitedCycleNanos(long refreshIntervalNanos, int refreshMilliHz, int fps, bool variable) =>
        fps <= 0 ? refreshIntervalNanos
        : variable ? Math.Max(refreshIntervalNanos, 1_000_000_000L / fps)
        : refreshIntervalNanos * Divisor(refreshMilliHz, fps);

    /// <summary>
    /// Tests whether a vblank on a fixed-rate output starts a new capped cycle.
    /// </summary>
    /// <param name="vblank">The sequence number of this vblank.</param>
    /// <param name="previousVblank">The sequence number of the previous vblank.</param>
    /// <param name="divisor">The divisor from <see cref="Divisor"/>.</param>
    /// <returns><see langword="true"/> when the two vblanks fall in different cycles, or when the divisor is 1 or less.</returns>
    public static bool FixedOpens(ulong vblank, ulong previousVblank, int divisor) =>
        divisor <= 1 || vblank / (ulong)divisor != previousVblank / (ulong)divisor;

    /// <summary>
    /// Tests whether a variable-rate cycle is open at a given time.
    /// </summary>
    /// <param name="nowNanos">The current monotonic time in nanoseconds.</param>
    /// <param name="anchorNanos">The start of the current cycle, or 0 when there is none.</param>
    /// <param name="cycleNanos">The cycle length in nanoseconds.</param>
    /// <returns><see langword="true"/> when there is no anchor, or the cycle ends within <see cref="EarlyAllowanceNanos"/>.</returns>
    public static bool VariableOpens(long nowNanos, long anchorNanos, long cycleNanos) =>
        anchorNanos == 0 || nowNanos + EarlyAllowanceNanos >= anchorNanos + cycleNanos;

    /// <summary>
    /// Computes the start of the next cycle after the surface used a frame. A use near the schedule keeps the schedule, so the cadence does not drift.
    /// </summary>
    /// <param name="usedNanos">The time in nanoseconds when the surface used the frame.</param>
    /// <param name="anchorNanos">The start of the current cycle, or 0 when there is none.</param>
    /// <param name="cycleNanos">The cycle length in nanoseconds.</param>
    /// <param name="refreshIntervalNanos">The output refresh interval in nanoseconds.</param>
    /// <returns>The scheduled cycle end when the use was on schedule, otherwise <paramref name="usedNanos"/>.</returns>
    public static long NextAnchor(long usedNanos, long anchorNanos, long cycleNanos, long refreshIntervalNanos)
    {
        var schedule = anchorNanos + cycleNanos;
        var onSchedule = anchorNanos != 0 && usedNanos + EarlyAllowanceNanos >= schedule && usedNanos < schedule + refreshIntervalNanos;
        return onSchedule ? schedule : usedNanos;
    }
}
