using System.Diagnostics;

namespace Basin;

public static class MonotonicClock
{
    private static readonly double NanosPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

    public static long Nanos => (long)(Stopwatch.GetTimestamp() * NanosPerTick);
}
