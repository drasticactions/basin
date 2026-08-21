namespace Basin.Capabilities;

public static class FrameClockExtensions
{
    public static void BeginFrameAtNextRefresh(this IFrameClock clock, IOutput output)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(output);
        var refresh = output.CurrentMode.RefreshMilliHz;
        clock.BeginFrame(output, refresh > 0 ? MonotonicClock.Nanos + (1_000_000_000_000L / refresh) : 0);
    }
}
