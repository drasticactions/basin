using Basin.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Basin.Cli;

public static class BasinLogging
{
    public static ILoggerFactory Create(LogLevel minimum)
    {
        var factory = new StandardErrorLoggerFactory(minimum);
        BasinLog.Level = ToBasinLevel(minimum);
        var bridge = factory.CreateLogger("basin");
        BasinLog.Sink = (severity, message) =>
            bridge.Log(ToLoggerLevel(severity), 0, message, null, static (state, _) => state);
        WaylandDiagnostics.RouteToBasinLog();
        return factory;
    }

    public static LogLevel ParseLevel(string name) => name switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "info" => LogLevel.Information,
        "warn" => LogLevel.Warning,
        "error" => LogLevel.Error,
        _ => throw new ArgumentException($"unknown log level '{name}'", nameof(name)),
    };

    private static BasinLogLevel ToBasinLevel(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => BasinLogLevel.Debug,
        LogLevel.Information => BasinLogLevel.Info,
        LogLevel.Warning => BasinLogLevel.Warn,
        LogLevel.Error or LogLevel.Critical => BasinLogLevel.Error,
        _ => BasinLogLevel.None,
    };

    private static LogLevel ToLoggerLevel(BasinLogLevel level) => level switch
    {
        BasinLogLevel.Debug => LogLevel.Debug,
        BasinLogLevel.Info => LogLevel.Information,
        BasinLogLevel.Warn => LogLevel.Warning,
        BasinLogLevel.Error => LogLevel.Error,
        _ => LogLevel.None,
    };
}
