using System.Runtime.CompilerServices;
using Basin.Diagnostics;

namespace Basin.Tests;

internal static class TestLogging
{
    [ModuleInitializer]
    internal static void Install()
    {
        Console.Out.Write(string.Empty);
        Console.Error.Write(string.Empty);
        BasinLog.Level = Environment.GetEnvironmentVariable("BASIN_TRACE") is null
            ? BasinLogLevel.Warn
            : BasinLogLevel.Debug;
        BasinLog.Sink = static (level, message) =>
            Console.Error.WriteLine($"[{level.ToString().ToLowerInvariant()}] basin: {message}");
    }
}
