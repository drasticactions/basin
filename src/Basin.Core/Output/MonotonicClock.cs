using System.Diagnostics;

namespace Basin;

/// <summary>
/// Reads the monotonic clock that basin uses for every frame and presentation time.
/// </summary>
public static class MonotonicClock
{
    private static readonly double NanosPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Gets the current monotonic time in nanoseconds. Only differences between two readings have a meaning.
    /// </summary>
    public static long Nanos => (long)(Stopwatch.GetTimestamp() * NanosPerTick);
}
