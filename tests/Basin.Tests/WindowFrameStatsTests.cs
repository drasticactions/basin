using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class WindowFrameStatsTests
{
    [Fact]
    public void A_window_presenting_every_frame_reports_each_present_and_its_latency()
    {
        using var host = new CompositorTestHost();
        var services = new BasinServices(host.Display, host.Loop);
        services.Use(host.Layout);
        services.Use(host.Compositor);
        using var stats = new WindowFrameStats(services);
        host.SceneOutput.Presented = host.Compositor.PresentedSets.For(host.Output);
        stats.Acquire();
        var window = MappedToplevel.Map(host, host.Client);

        for (var i = 0; i < 30; i++)
        {
            FifoTests.Paint(host.Client, window.Surface, 40 + (i % 2));
            window.Surface.Commit();
            host.PumpToServer();
            host.CommitFrame();
        }

        Assert.True(stats.TryGet(window.ServerSurface, MonotonicClock.Nanos, out var snapshot));
        Assert.InRange(snapshot.Presents, 29, 30);
        Assert.InRange(snapshot.Commits, 29, 30);
        Assert.Same(host.Output, snapshot.Output);
        Assert.Equal(60_000, snapshot.RefreshMilliHz);
        Assert.True(snapshot.MeanLatencyMs >= 0);
        Assert.True(snapshot.P99PresentIntervalMs >= snapshot.MeanPresentIntervalMs * 0.5);
    }

    [Fact]
    public void A_frame_with_no_new_commit_is_not_a_present()
    {
        using var host = new CompositorTestHost();
        var services = new BasinServices(host.Display, host.Loop);
        services.Use(host.Layout);
        services.Use(host.Compositor);
        using var stats = new WindowFrameStats(services);
        host.SceneOutput.Presented = host.Compositor.PresentedSets.For(host.Output);
        stats.Acquire();
        var window = MappedToplevel.Map(host, host.Client);
        host.CommitFrame();
        FifoTests.Paint(host.Client, window.Surface, 40);
        window.Surface.Commit();
        host.PumpToServer();
        host.CommitFrame();
        for (var i = 0; i < 10; i++)
        {
            host.SceneOutput.Ring.AddWhole();
            host.CommitFrame();
        }

        Assert.True(stats.TryGet(window.ServerSurface, MonotonicClock.Nanos, out var snapshot));
        Assert.Equal(1, snapshot.Presents);
    }

    [Fact]
    public void Releasing_the_last_holder_stops_tracking()
    {
        using var host = new CompositorTestHost();
        var services = new BasinServices(host.Display, host.Loop);
        services.Use(host.Layout);
        services.Use(host.Compositor);
        using var stats = new WindowFrameStats(services);
        host.SceneOutput.Presented = host.Compositor.PresentedSets.For(host.Output);
        stats.Acquire();
        var window = MappedToplevel.Map(host, host.Client);
        host.CommitFrame();
        Assert.True(stats.TryGet(window.ServerSurface, MonotonicClock.Nanos, out _));
        stats.Release();
        Assert.False(stats.IsActive);
        Assert.False(stats.TryGet(window.ServerSurface, MonotonicClock.Nanos, out _));
    }
}
