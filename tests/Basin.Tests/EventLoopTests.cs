using Basin.Diagnostics;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class EventLoopTests : IDisposable
{
    private readonly WlServerDisplay _display;
    private readonly WaylandEventLoop _loop;

    public EventLoopTests()
    {
        CompositorTestHost.SkipWithoutWaylandServer();
        BasinCounters.Reset();
        _display = CompositorTestHost.TransportUnderTest == Basin.Cli.TransportKind.Managed
            ? WlServerDisplay.Create(new ManagedTransport())
            : WlServerDisplay.Create();
        _loop = new WaylandEventLoop(_display);
    }

    public void Dispose() => _display.Dispose();

    [Fact]
    public void Idle_callbacks_drain_at_the_top_of_the_next_iteration()
    {
        var order = new List<string>();
        _loop.AddIdle(() => order.Add("first"));
        _loop.AddIdle(() =>
        {
            order.Add("second");
            _loop.AddIdle(() => order.Add("queued-during-drain"));
        });

        _loop.Dispatch(0);
        Assert.Equal(["first", "second"], order);

        _loop.Dispatch(0);
        Assert.Equal(["first", "second", "queued-during-drain"], order);
    }

    [Fact]
    public void Deferred_destruction_runs_on_the_next_iteration()
    {
        var disposed = false;
        var victim = new DisposeProbe(() => disposed = true);

        _loop.DeferDestroy(victim);
        LeakTracking.Expect(1, BasinCounters.PendingFrees);
        Assert.False(disposed);

        _loop.Dispatch(0);
        Assert.True(disposed);
        LeakTracking.Expect(0, BasinCounters.PendingFrees);
    }

    [Fact]
    public void Destroy_deferred_from_inside_dispatch_survives_the_frame()
    {
        var disposed = false;
        _loop.AddIdle(() => _loop.DeferDestroy(new DisposeProbe(() => disposed = true)));

        _loop.Dispatch(0);
        Assert.False(disposed);
        _loop.Dispatch(0);
        Assert.True(disposed);
    }

    [Fact]
    public void Timer_source_fires_and_balances_counters()
    {
        LeakTracking.Require();
        var fired = 0;
        var timer = _loop.AddTimer(() => fired++);
        Assert.Equal(1, BasinCounters.LiveObjects);

        timer.UpdateTimer(1);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (fired == 0 && deadline.ElapsedMilliseconds < 5000)
        {
            _loop.Dispatch(50);
        }

        Assert.Equal(1, fired);

        timer.Remove();
        Assert.Equal(0, BasinCounters.LiveObjects);
    }

    private sealed class DisposeProbe(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
