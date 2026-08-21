using Basin.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Basin.Cli;

internal sealed class StandardErrorLogger(string category, LogLevel minimum) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minimum;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        Console.Error.WriteLine(exception is null
            ? $"[{Label(logLevel)}] {category}: {message}"
            : $"[{Label(logLevel)}] {category}: {message}{Environment.NewLine}{exception}");
    }

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        _ => "critical",
    };
}
