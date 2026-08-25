using System.Runtime.CompilerServices;
using Basin.Diagnostics;

namespace Basin.Tests;

internal static class TestLogging
{
    private static readonly StandardErrorLogSink Sink = new();

    [ModuleInitializer]
    internal static void Install()
    {
        BasinLog.Level = Environment.GetEnvironmentVariable("BASIN_TRACE") is null
            ? BasinLogLevel.Warn
            : BasinLogLevel.Debug;
        BasinLog.Sink = Sink;
        WarmStreams();
    }

    internal static void WarmStreams()
    {
        BasinReport.Flush();
        Sink.Flush();
    }
}
