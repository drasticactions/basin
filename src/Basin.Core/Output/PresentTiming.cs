namespace Basin;

public static class PresentTiming
{
    public const long MaxToleranceNanos = 50_000;

    public static long Tolerance(long intervalNanos) =>
        intervalNanos <= 0 ? MaxToleranceNanos : Math.Min(MaxToleranceNanos, intervalNanos / 100);

    public static long Tolerance(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Tolerance(output.CurrentMode.RefreshIntervalNanoseconds);
    }

    public static long GridOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.AdaptiveSync ? 0 : output.CurrentMode.RefreshIntervalNanoseconds;
    }

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

    public static long PredictFixedLatch(long now, long target, long wakeup, long interval, bool inPass)
    {
        var lead = !inPass && target > wakeup ? target - wakeup : 0;
        return PredictFixed(Add(now, lead), target, interval);
    }

    public static long Predict(long now, long offset, long last, long interval) =>
        Math.Max(Add(now, offset), Add(last, interval));

    public static long? TargetWake(long target, long offset) => target > offset ? target - offset : null;

    private static long Add(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
}
