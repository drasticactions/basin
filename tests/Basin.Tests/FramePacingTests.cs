using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class FifoTests
{
    [Fact]
    public void Two_barriered_updates_land_on_two_successive_refresh_cycles()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var fifo = BindFifo(host, client).GetFifo(surface);

        Paint(client, surface, 10);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 10);

        Paint(client, surface, 20);
        fifo.WaitBarrier();
        fifo.SetBarrier();
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(10, server!.Current.Width);
        Assert.True(server.HasParkedCommits);

        Paint(client, surface, 30);
        fifo.WaitBarrier();
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(10, server.Current.Width);

        host.Output.StepFrame();
        Assert.Equal(20, server.Current.Width);
        Assert.True(server.HasParkedCommits);

        host.Output.StepFrame();
        Assert.Equal(30, server.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void An_unbarriered_update_applies_without_waiting()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var fifo = BindFifo(host, client).GetFifo(surface);

        Paint(client, surface, 12);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 12);

        Paint(client, surface, 24);
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(24, server!.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void The_latch_ends_with_the_barrier_clear_so_a_later_update_applies_on_arrival()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var fifo = BindFifo(host, client).GetFifo(surface);

        Paint(client, surface, 10);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 10);
        Assert.True(manager.HasPendingBarriers);

        manager.Latch();
        Paint(client, surface, 20);
        fifo.WaitBarrier();
        fifo.SetBarrier();
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(20, server!.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void A_parked_update_comes_out_behind_the_latch_that_retired_its_barrier()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var fifo = BindFifo(host, client).GetFifo(surface);

        Paint(client, surface, 10);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 10);

        Paint(client, surface, 20);
        fifo.WaitBarrier();
        fifo.SetBarrier();
        surface.Commit();
        host.PumpToServer();
        Assert.True(server!.HasParkedCommits);

        manager.Latch();
        Assert.Equal(10, server.Current.Width);
        Assert.True(server.HasParkedCommits);

        host.Output.StepFrame();
        Assert.Equal(10, server.Current.Width);

        manager.Latch();
        Assert.Equal(20, server.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void A_commit_timing_target_lands_in_the_frame_that_presents_at_its_time()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);
        using var timing = new CommitTimingManager(host.Display, host.Compositor, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var fifo = BindFifo(host, client).GetFifo(surface);

        var target = MonotonicClock.Nanos + 200_000_000;
        Paint(client, surface, 10);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 10);

        Paint(client, surface, 20);
        fifo.WaitBarrier();
        fifo.SetBarrier();
        SetTimestamp(host, client, surface, target);
        surface.Commit();
        host.PumpToServer();
        Assert.True(server!.HasParkedCommits);

        manager.Latch(target - 16_000_000);
        Assert.Equal(10, server.Current.Width);
        manager.Latch(target);
        Assert.Equal(20, server.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    private static void SetTimestamp(CompositorTestHost host, ShmTestClient client, WlSurface surface, long nanos)
    {
        Basin.Desktop.Protocol.WpCommitTimingManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_commit_timing_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpCommitTimingManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        var timer = proxy!.GetTimer(surface);
        var seconds = (ulong)(nanos / 1_000_000_000);
        timer.SetTimestamp((uint)(seconds >> 32), (uint)seconds, (uint)(nanos % 1_000_000_000));
    }

    [Fact]
    public void A_parked_update_with_no_flips_and_no_latches_still_makes_progress()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var fifo = BindFifo(host, client).GetFifo(surface);

        Paint(client, surface, 10);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 10);

        Paint(client, surface, 20);
        fifo.WaitBarrier();
        surface.Commit();
        host.PumpToServer();
        Assert.True(server!.HasParkedCommits);

        var deadline = Environment.TickCount64 + 2_000;
        while (server.HasParkedCommits && Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(5);
        }

        Assert.Equal(20, server.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void A_surface_destroyed_with_a_parked_commit_leaks_nothing()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var fifo = BindFifo(host, client).GetFifo(surface);

        Paint(client, surface, 16);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 16);

        Paint(client, surface, 32);
        fifo.WaitBarrier();
        surface.Commit();
        host.PumpToServer();
        Assert.True(server!.HasParkedCommits);

        fifo.Destroy();
        surface.Dispose();
        host.PumpToServer();
        Assert.True(server.IsDestroyed);
    }

    [Fact]
    public void A_second_fifo_object_for_one_surface_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var proxy = BindFifo(host, client);
        var first = proxy.GetFifo(surface);
        host.PumpToServer();
        proxy.GetFifo(surface);
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("wp_fifo_manager_v1", error.Message, StringComparison.Ordinal);
        GC.KeepAlive(first);
    }

    internal static void Paint(ShmTestClient client, WlSurface surface, int size)
    {
        var buffer = client.CreateBuffer(size, size, Fill.Solid(size, size, 0xFF204060));
        surface.Attach(buffer.Proxy, 0, 0);
        surface.Damage(0, 0, size, size);
    }

    private static Basin.Desktop.Protocol.WpFifoManagerV1 BindFifo(CompositorTestHost host, ShmTestClient client)
    {
        Basin.Desktop.Protocol.WpFifoManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_fifo_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpFifoManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}

public sealed class CommitExtensionStateTests
{
    private sealed class Payload : IDisposable
    {
        public Payload(string tag, List<string> disposed) => (Tag, Disposed) = (tag, disposed);

        public string Tag { get; }

        private List<string> Disposed { get; }

        public void Dispose() => Disposed.Add(Tag);
    }

    [Fact]
    public void A_payload_rides_a_parked_commit_and_arrives_when_it_applies()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);

        var client = host.Client;
        var disposed = new List<string>();
        Surface? server = null;

        host.Compositor.SurfaceCreated += s =>
        {
            server ??= s;
            s.CommitRequested += () => s.Pending.SetExtension(new Payload($"c{++_commits}", disposed));
        };

        var surface = client.Compositor.CreateSurface();
        var fifo = BindFifo(host, client).GetFifo(surface);

        FifoTests.Paint(client, surface, 10);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 10);
        Assert.Equal("c1", server!.Current.GetExtension<Payload>()?.Tag);

        FifoTests.Paint(client, surface, 20);
        fifo.WaitBarrier();
        fifo.SetBarrier();
        surface.Commit();
        FifoTests.Paint(client, surface, 30);
        fifo.WaitBarrier();
        surface.Commit();
        host.PumpToServer();
        Assert.Equal("c1", server.Current.GetExtension<Payload>()?.Tag);

        host.Output.StepFrame();
        Assert.Equal(20, server.Current.Width);
        Assert.Equal("c2", server.Current.GetExtension<Payload>()?.Tag);

        host.Output.StepFrame();
        Assert.Equal(30, server.Current.Width);
        Assert.Equal("c3", server.Current.GetExtension<Payload>()?.Tag);

        Assert.Equal(["c1", "c2"], disposed);
    }

    [Fact]
    public void A_parked_commit_destroyed_unapplied_disposes_what_it_carried()
    {
        using var host = new CompositorTestHost();
        using var manager = new FifoManager(host.Display, host.Compositor, host.Layout, host.Loop);

        var client = host.Client;
        var disposed = new List<string>();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s =>
        {
            server ??= s;
            s.CommitRequested += () => s.Pending.SetExtension(new Payload($"c{++_commits}", disposed));
        };

        var surface = client.Compositor.CreateSurface();
        var fifo = BindFifo(host, client).GetFifo(surface);

        FifoTests.Paint(client, surface, 12);
        fifo.SetBarrier();
        surface.Commit();
        host.PumpUntil(() => server?.Current.Width == 12);

        FifoTests.Paint(client, surface, 24);
        fifo.WaitBarrier();
        surface.Commit();
        host.PumpToServer();
        Assert.True(server!.HasParkedCommits);

        fifo.Destroy();
        surface.Dispose();
        host.PumpToServer();

        Assert.Contains("c2", disposed);
    }

    private int _commits;

    private static Basin.Desktop.Protocol.WpFifoManagerV1 BindFifo(CompositorTestHost host, ShmTestClient client)
    {
        Basin.Desktop.Protocol.WpFifoManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_fifo_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpFifoManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}

public sealed class CommitTimingTests
{
    [Fact]
    public void A_future_timestamp_parks_the_update_until_it_arrives()
    {
        using var host = new CompositorTestHost();
        using var manager = new CommitTimingManager(host.Display, host.Compositor, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var timer = BindTiming(host, client).GetTimer(surface);

        FifoTests.Paint(client, surface, 14);
        SetTimestamp(timer, MonotonicClock.Nanos + 20_000_000);
        surface.Commit();
        host.PumpToServer();
        Assert.NotNull(server);
        Assert.True(server!.HasParkedCommits);
        Assert.Equal(0, server.Current.Width);

        var deadline = MonotonicClock.Nanos + 2_000_000_000;
        while (server.HasParkedCommits && MonotonicClock.Nanos < deadline)
        {
            host.Loop.Dispatch(5);
        }

        Assert.Equal(14, server.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void A_past_timestamp_applies_immediately()
    {
        using var host = new CompositorTestHost();
        using var manager = new CommitTimingManager(host.Display, host.Compositor, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;
        var timer = BindTiming(host, client).GetTimer(surface);

        FifoTests.Paint(client, surface, 18);
        SetTimestamp(timer, MonotonicClock.Nanos - 1_000_000_000);
        surface.Commit();
        host.PumpToServer();

        Assert.Equal(18, server!.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void Two_timestamps_on_one_content_update_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new CommitTimingManager(host.Display, host.Compositor, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var timer = BindTiming(host, client).GetTimer(surface);

        var target = MonotonicClock.Nanos + 1_000_000_000;
        SetTimestamp(timer, target);
        SetTimestamp(timer, target);
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("wp_commit_timer_v1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_out_of_range_nanosecond_field_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new CommitTimingManager(host.Display, host.Compositor, host.Loop);

        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        var timer = BindTiming(host, client).GetTimer(surface);

        timer.SetTimestamp(0, 1, 1_000_000_000);
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("wp_commit_timer_v1", error.Message, StringComparison.Ordinal);
    }

    private static void SetTimestamp(Basin.Desktop.Protocol.WpCommitTimerV1 timer, long nanos)
    {
        var seconds = (ulong)(nanos / 1_000_000_000);
        timer.SetTimestamp((uint)(seconds >> 32), (uint)seconds, (uint)(nanos % 1_000_000_000));
    }

    private static Basin.Desktop.Protocol.WpCommitTimingManagerV1 BindTiming(CompositorTestHost host, ShmTestClient client)
    {
        Basin.Desktop.Protocol.WpCommitTimingManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_commit_timing_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpCommitTimingManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}

public sealed class HeldCommitTests
{
    [Fact]
    public void A_held_commit_waits_for_its_holder_and_for_nothing_else()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;

        FifoTests.Paint(client, surface, 10);
        surface.Commit();
        host.PumpToServer();
        Assert.NotNull(server);
        Assert.Equal(10, server!.Current.Width);

        server.HoldNextCommit();
        FifoTests.Paint(client, surface, 20);
        surface.Commit();
        host.PumpToServer();

        Assert.True(server.HasParkedCommits);
        Assert.Equal(10, server.Current.Width);

        Assert.False(server.ReleaseParkedCommits(MonotonicClock.Nanos, refreshCycleCompleted: true));
        Assert.Equal(10, server.Current.Width);

        Assert.True(server.ReleaseHeldCommits());
        Assert.Equal(20, server.Current.Width);
        Assert.False(server.HasParkedCommits);

        FifoTests.Paint(client, surface, 30);
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(30, server.Current.Width);
        Assert.False(server.HasParkedCommits);
    }

    [Fact]
    public void A_commit_behind_a_held_one_does_not_overtake_it()
    {
        using var host = new CompositorTestHost();
        var client = host.Client;
        var surface = client.Compositor.CreateSurface();
        Surface? server = null;
        host.Compositor.SurfaceCreated += s => server ??= s;

        FifoTests.Paint(client, surface, 10);
        surface.Commit();
        host.PumpToServer();

        server!.HoldNextCommit();
        FifoTests.Paint(client, surface, 20);
        surface.Commit();
        FifoTests.Paint(client, surface, 30);
        surface.Commit();
        host.PumpToServer();
        Assert.Equal(10, server.Current.Width);

        Assert.True(server.ReleaseHeldCommits());
        Assert.Equal(30, server.Current.Width);
        Assert.False(server.HasParkedCommits);
    }
}
