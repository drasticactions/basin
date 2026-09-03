using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class LoggingAllocationTests
{
    private static readonly BasinLogger Log = BasinLog.For("alloc-test");

    private static long Measure(Action log)
    {
        log();
        log();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16; i++)
        {
            log();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void A_debug_line_to_the_standard_error_sink_allocates_nothing()
    {
        TestLogging.WarmStreams();
        var previousSink = BasinLog.Sink;
        var previousLevel = BasinLog.Level;
        BasinLog.Sink = new StandardErrorLogSink();
        BasinLog.Level = BasinLogLevel.Debug;
        try
        {
            var what = "flip";
            long t0 = 1234567890123, t1 = t0 + 100_000, t2 = t1 + 200_000;
            var flag = true;
            Assert.Equal(0, Measure(() => Log.Debug($"literal only")));
            Assert.Equal(0, Measure(() => Log.Debug($"{what} issue={t2 / 1_000_000}")));
            Assert.Equal(0, Measure(() => Log.Debug($"testMs={(t1 - t0) / 1_000_000.0:F1} ioctlMs={(t2 - t1) / 1_000_000.0:F1}")));
            Assert.Equal(0, Measure(() => Log.Debug($"scheduler: repaint queued (inFlight={flag} timer={flag})")));
            Assert.Equal(0, Measure(() => Log.Debug($"{what} issue={t2 / 1_000_000} testMs={(t1 - t0) / 1_000_000.0:F1} ioctlMs={(t2 - t1) / 1_000_000.0:F1}")));
            var count = 7;
            var ratio = 0.25f;
            uint serial = 42;
            Assert.Equal(0, Measure(() => Log.Debug($"placed {count}/{count} serial={serial} ratio={ratio:F2} at {ratio}")));
            Assert.Equal(0, Measure(() => BasinReport.Line($"FRAMES {count} avg={ratio:F3} flag={flag}")));
        }
        finally
        {
            BasinLog.Sink = previousSink;
            BasinLog.Level = previousLevel;
        }
    }
}
