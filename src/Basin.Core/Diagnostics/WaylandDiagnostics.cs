using Wayland;
using Wayland.Server;

namespace Basin.Diagnostics;

public static class WaylandDiagnostics
{
    private static readonly BasinLogger Log = BasinLog.For("wayland");

    private static bool _routed;

    public static void RouteToBasinLog()
    {
        if (_routed || !WaylandLog.IsSupported)
        {
            return;
        }

        _routed = true;
        try
        {
            WaylandLog.SetHandler(WaylandLogSide.Server, static message => Log.Warn($"libwayland: {message}"));
            WaylandLog.SetHandler(WaylandLogSide.Client, static message => Log.Warn($"libwayland-client: {message}"));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    public static IDisposable TraceProtocol(WlServerDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return display.AddProtocolLogger(static (in WlProtocolMessage message) =>
        {
            if (BasinLog.IsEnabled(BasinLogLevel.Debug))
            {
                var line = message.ToString();
                Log.Debug($"{line}");
            }
        });
    }
}
