namespace Basin.Capabilities;

/// <summary>
/// Helpers for an <see cref="IFrameClock"/>.
/// </summary>
public static class FrameClockExtensions
{
    /// <summary>
    /// Begins a frame with a predicted vblank one refresh interval from now. A mode with no refresh rate gives 0, which
    /// sinks read as "apply now".
    /// </summary>
    /// <param name="clock">The clock to begin the frame on.</param>
    /// <param name="output">The output whose current mode sets the refresh interval.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> or <paramref name="output"/> is <see langword="null"/>.</exception>
    public static void BeginFrameAtNextRefresh(this IFrameClock clock, IOutput output)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(output);
        var refresh = output.CurrentMode.RefreshMilliHz;
        clock.BeginFrame(output, refresh > 0 ? MonotonicClock.Nanos + (1_000_000_000_000L / refresh) : 0);
    }
}
