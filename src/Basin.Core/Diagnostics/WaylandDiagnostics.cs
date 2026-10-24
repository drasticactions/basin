using Wayland;
using Wayland.Server;

namespace Basin.Diagnostics;

/// <summary>
/// Connects libwayland diagnostics to the basin log.
/// </summary>
public static class WaylandDiagnostics
{
    private static readonly BasinLogger Log = BasinLog.For("wayland");

    private static bool _routed;

    /// <summary>
    /// Sends the server and client log output of libwayland to the <c>wayland</c> category as warnings. A locked socket name, which libwayland meets when it looks for a free name, logs at debug. Only the first call has an effect, and a missing native entry point is ignored.
    /// </summary>
    public static void RouteToBasinLog()
    {
        if (_routed || !WaylandLog.IsSupported)
        {
            return;
        }

        _routed = true;
        try
        {
            WaylandLog.SetHandler(WaylandLogSide.Server, static message => Server(message.AsSpan().TrimEnd('\n')));
            WaylandLog.SetHandler(WaylandLogSide.Client, static message => Log.Warn($"libwayland-client: {message.AsSpan().TrimEnd('\n')}"));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Debug($"libwayland: no log handler entry point, its log stays on stderr");
        }
    }

    /// <summary>
    /// Logs each protocol message of a display at the debug level. Allocation scopes are disabled until the trace is disposed, because each message allocates.
    /// </summary>
    /// <param name="display">The display to trace.</param>
    /// <returns>The handle that stops the trace and restores <see cref="AllocationScope.Enabled"/>.</returns>
    public static IDisposable TraceProtocol(WlServerDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        var logger = display.AddProtocolLogger(static (in WlProtocolMessage message) =>
        {
            if (BasinLog.IsEnabled(BasinLogLevel.Debug))
            {
                var line = message.ToString();
                Log.Debug($"{line}");
            }
        });
        return new ProtocolTrace(logger);
    }

    private static void Server(ReadOnlySpan<char> message)
    {
        if (message.StartsWith("unable to lock lockfile", StringComparison.Ordinal))
        {
            Log.Debug($"libwayland: {message}");
            return;
        }

        Log.Warn($"libwayland: {message}");
    }

    private sealed class ProtocolTrace : IDisposable
    {
        private readonly IDisposable _logger;
        private readonly bool _checked;
        private bool _disposed;

        public ProtocolTrace(IDisposable logger)
        {
            _logger = logger;
            _checked = AllocationScope.Enabled;
            AllocationScope.Enabled = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _logger.Dispose();
            AllocationScope.Enabled = _checked;
        }
    }
}
