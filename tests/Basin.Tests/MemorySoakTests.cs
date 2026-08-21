using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

[Trait("kind", "soak")]
public sealed class MemorySoakTests
{
    private const int WarmUpCycles = 20;

    private const int MeasuredCycles = 200;

    private const long RetainedBytesPerCycleCeiling = 64;

    [Fact]
    public void Client_churn_retains_nothing()
    {
        SkipUnlessRequested();
        using var host = new CompositorTestHost();

        Soak(host, "client churn", static h =>
        {
            int serverFd, clientFd;
            unsafe
            {
                var fds = stackalloc int[2];
                if (socketpair(AF_UNIX, SOCK_STREAM, 0, fds) != 0)
                {
                    throw new InvalidOperationException("socketpair failed");
                }

                serverFd = fds[0];
                clientFd = fds[1];
            }

            h.Display.CreateClient(serverFd);
            h.Loop.Dispatch(0);
            _ = close(clientFd);
            h.Loop.Dispatch(0);
            h.Loop.Dispatch(0);
        });
    }

    [Fact]
    public void Toplevel_churn_retains_nothing()
    {
        SkipUnlessRequested();
        using var host = new CompositorTestHost();
        var client = host.Client;

        Soak(host, "toplevel churn", h =>
        {
            var surface = client.Compositor.CreateSurface();
            var xdgSurface = client.WmBase!.GetXdgSurface(surface);
            var toplevel = xdgSurface.GetToplevel();
            surface.Commit();
            h.PumpToClient();

            toplevel.Dispose();
            xdgSurface.Dispose();
            surface.Dispose();
            h.PumpToClient();
        });
    }

    [Fact]
    public void A_long_run_of_frames_retains_nothing()
    {
        SkipUnlessRequested();
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        var buffer = host.Client.CreateBuffer(40, 40, Fill.Solid(40, 40, 0xff336699));

        Soak(host, "frames", h =>
        {
            using var callback = window.Surface.Frame();
            window.Surface.Attach(buffer.Proxy, 0, 0);
            window.Surface.Damage(0, 0, 40, 40);
            window.Surface.Commit();
            h.PumpToClient();
            h.Output.StepFrame();
            h.RenderFrame();
        });
    }

    private static void Soak(CompositorTestHost host, string what, Action<CompositorTestHost> cycle)
    {
        for (var i = 0; i < WarmUpCycles; i++)
        {
            cycle(host);
        }

        var baselineCensus = new Dictionary<string, int>();
        BasinCounters.SnapshotCensus(baselineCensus);
        var baselineLive = BasinCounters.LiveObjects;
        var baselineRetained = Retained();

        for (var i = 0; i < MeasuredCycles; i++)
        {
            cycle(host);
        }

        var census = new Dictionary<string, int>();
        BasinCounters.SnapshotCensus(census);
        var retained = Retained();

        var grown = new List<string>();
        foreach (var (file, live) in census)
        {
            var was = baselineCensus.GetValueOrDefault(file);
            if (live > was)
            {
                grown.Add($"{file}: {was} -> {live}");
            }
        }

        Assert.True(
            grown.Count == 0,
            $"{what}: tracked objects accumulated over {MeasuredCycles} cycles:"
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, grown)}");

        if (BasinCounters.Enabled)
        {
            Assert.Equal(baselineLive, BasinCounters.LiveObjects);
        }

        var perCycle = (retained - baselineRetained) / (double)MeasuredCycles;
        Assert.True(
            perCycle <= RetainedBytesPerCycleCeiling,
            $"{what}: retained managed memory grew {perCycle:F1} bytes per cycle, ceiling "
            + $"{RetainedBytesPerCycleCeiling} ({baselineRetained} -> {retained} over {MeasuredCycles} cycles)");
    }

    private static long Retained()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static void SkipUnlessRequested() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("BASIN_SOAK") is "1" or "true",
            "soak tests run only when BASIN_SOAK=1");

    private const int AF_UNIX = 1;

    private const int SOCK_STREAM = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
