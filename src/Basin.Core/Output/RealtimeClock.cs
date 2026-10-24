namespace Basin;

/// <summary>
/// Reads the wall clock.
/// </summary>
public static class RealtimeClock
{
    /// <summary>
    /// Gets the current UTC time in nanoseconds since the Unix epoch, with a resolution of 100 nanoseconds.
    /// </summary>
    public static long Nanos => (DateTime.UtcNow - DateTime.UnixEpoch).Ticks * 100;
}
