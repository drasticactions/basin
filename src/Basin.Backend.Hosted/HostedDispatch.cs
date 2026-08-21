using Basin.Capabilities;
using Basin.Scene;
using Wayland.Server;

namespace Basin.Backend.Hosted;

internal static class HostedDispatch
{
    public static void Run(
        ICompositorEventLoop loop,
        TimeSpan warning,
        TimeSpan limit,
        Action<TimeSpan>? overran,
        Action<TimeSpan>? exceeded)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        loop.Dispatch(0);
        var spent = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        if (spent > limit)
        {
            exceeded?.Invoke(spent);
        }
        else if (spent > warning)
        {
            overran?.Invoke(spent);
        }
    }
}
