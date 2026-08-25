using Basin.Diagnostics;

namespace Basin.Cli;

public static class BasinLogging
{
    public static void RouteToStandardError(BasinLogLevel minimum)
    {
        BasinLog.Level = minimum;
        BasinLog.Sink = new StandardErrorLogSink();
        WaylandDiagnostics.RouteToBasinLog();
    }

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
