using Basin.Diagnostics;

namespace Basin.Cli;

/// <summary>
/// Connects the basin log to the process standard error.
/// </summary>
public static class BasinLogging
{
    /// <summary>
    /// Sets the minimum <see cref="BasinLog"/> level, writes the log to standard error and routes libwayland diagnostics into the same log.
    /// </summary>
    /// <param name="minimum">The lowest level that the log writes.</param>
    public static void RouteToStandardError(BasinLogLevel minimum)
    {
        BasinLog.Level = minimum;
        BasinLog.Sink = new StandardErrorLogSink();
        WaylandDiagnostics.RouteToBasinLog();
    }

    /// <summary>
    /// Parses a <c>--log-level</c> name: <c>trace</c>, <c>debug</c>, <c>info</c>, <c>warn</c> or <c>error</c>. The match is case-sensitive.
    /// </summary>
    /// <param name="name">The level name.</param>
    /// <returns>The matching level.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a known level.</exception>
    public static BasinLogLevel ParseLevel(string name) => name switch
    {
        "trace" => BasinLogLevel.Trace,
        "debug" => BasinLogLevel.Debug,
        "info" => BasinLogLevel.Info,
        "warn" => BasinLogLevel.Warn,
        "error" => BasinLogLevel.Error,
        _ => throw new ArgumentException($"unknown log level '{name}'", nameof(name)),
    };
}
