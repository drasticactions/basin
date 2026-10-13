namespace Basin.Desktop;

public static class FrameLimitCadence
{
    public const long EarlyAllowanceNanos = 200_000;

    public static int Divisor(int refreshMilliHz, int fps)
    {
        if (fps <= 0 || refreshMilliHz <= 0)
        {
            return 1;
        }

        var refreshHz = (int)Math.Round(refreshMilliHz / 1000.0);
        return Math.Max(1, refreshHz / Math.Min(fps, refreshHz));
    }

    public static long LimitedCycleNanos(long refreshIntervalNanos, int refreshMilliHz, int fps, bool variable) =>
        fps <= 0 ? refreshIntervalNanos
        : variable ? Math.Max(refreshIntervalNanos, 1_000_000_000L / fps)
        : refreshIntervalNanos * Divisor(refreshMilliHz, fps);

    public static bool FixedOpens(ulong vblank, ulong previousVblank, int divisor) =>
        divisor <= 1 || vblank / (ulong)divisor != previousVblank / (ulong)divisor;

    public static bool VariableOpens(long nowNanos, long anchorNanos, long cycleNanos) =>
        anchorNanos == 0 || nowNanos + EarlyAllowanceNanos >= anchorNanos + cycleNanos;

    public static long NextAnchor(long usedNanos, long anchorNanos, long cycleNanos, long refreshIntervalNanos)
    {
        var schedule = anchorNanos + cycleNanos;
        var onSchedule = anchorNanos != 0 && usedNanos + EarlyAllowanceNanos >= schedule && usedNanos < schedule + refreshIntervalNanos;
        return onSchedule ? schedule : usedNanos;
    }
}
