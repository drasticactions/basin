namespace Basin.Diagnostics;

/// <summary>
/// Switches for the diagnostic output of a basin process.
/// </summary>
public static class BasinDiagnostics
{
    /// <summary>
    /// The environment variable that turns on trace output when it is set to any value.
    /// </summary>
    public const string TraceVariable = "BASIN_TRACE";

    private static readonly bool TraceRequested = Environment.GetEnvironmentVariable(TraceVariable) is not null;

    /// <summary>
    /// Gets a value indicating whether <see cref="TraceVariable"/> was set at startup or the log level is <see cref="BasinLogLevel.Trace"/>.
    /// </summary>
    public static bool TraceEnabled => TraceRequested || BasinLog.Level == BasinLogLevel.Trace;

    /// <summary>
    /// Sends basin and libwayland log output to standard error through a new <see cref="StandardErrorLogSink"/>.
    /// </summary>
    /// <param name="level">The threshold to set. When it is <see langword="null"/>, the threshold is Debug with trace on and Warn with trace off.</param>
    public static void RouteToStandardError(BasinLogLevel? level = null)
    {
        BasinLog.Level = level ?? (TraceEnabled ? BasinLogLevel.Debug : BasinLogLevel.Warn);
        BasinLog.Sink = new StandardErrorLogSink();
        WaylandDiagnostics.RouteToBasinLog();
    }
}
