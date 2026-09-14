using Basin.Diagnostics;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class CompositorSynchronizationContextTests : IDisposable
{
    private readonly WlServerDisplay _display;
    private readonly WaylandEventLoop _loop;

    public CompositorSynchronizationContextTests()
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
    public void A_post_from_another_thread_runs_on_the_loop_thread_in_order()
    {
        using var context = new CompositorSynchronizationContext(_loop);
        var loopThread = Environment.CurrentManagedThreadId;
        var ran = new List<(int Value, int Thread, bool CurrentIsContext)>();
        using var posted = new ManualResetEventSlim();

        var worker = new Thread(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                context.Post(state => ran.Add(((int)state!, Environment.CurrentManagedThreadId, ReferenceEquals(SynchronizationContext.Current, context))), i);
            }

            posted.Set();
        });
        worker.Start();
        Assert.True(posted.Wait(5000, TestContext.Current.CancellationToken));
        worker.Join();
        Assert.Empty(ran);
        Assert.Equal(3, context.Pending);

        _loop.Dispatch(0);
        Assert.Equal([0, 1, 2], ran.Select(r => r.Value));
        Assert.All(ran, r => Assert.Equal(loopThread, r.Thread));
        Assert.All(ran, r => Assert.True(r.CurrentIsContext));
        Assert.Equal(0, context.Pending);
    }

    [Fact]
    public void Send_runs_inline_on_the_loop_thread_and_throws_elsewhere()
    {
        using var context = new CompositorSynchronizationContext(_loop);
        var ran = false;
        context.Send(_ => ran = true, null);
        Assert.True(ran);

        Exception? error = null;
        var worker = new Thread(() =>
        {
            try
            {
                context.Send(_ => { }, null);
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public void An_await_inside_a_posted_callback_resumes_on_the_loop()
    {
        using var context = new CompositorSynchronizationContext(_loop);
        var loopThread = Environment.CurrentManagedThreadId;
        var source = new TaskCompletionSource<int>();
        var resumedOn = 0;
        var done = false;

        context.Post(async _ =>
        {
            var value = await source.Task;
            resumedOn = Environment.CurrentManagedThreadId;
            done = value == 7;
        }, null);

        _loop.Dispatch(0);
        Assert.False(done);
        var completer = new Thread(() => source.SetResult(7));
        completer.Start();
        completer.Join();
        Assert.False(done);

        for (var i = 0; i < 10 && !done; i++)
        {
            _loop.Dispatch(10);
        }

        Assert.True(done);
        Assert.Equal(loopThread, resumedOn);
    }

    [Fact]
    public void Disposal_releases_the_pipe_and_drops_what_was_queued()
    {
        var context = new CompositorSynchronizationContext(_loop);
        var ran = false;
        context.Post(_ => ran = true, null);
        context.Dispose();
        _loop.Dispatch(0);
        Assert.False(ran);
        context.Post(_ => ran = true, null);
        _loop.Dispatch(0);
        Assert.False(ran);
        LeakTracking.Expect(0, BasinCounters.LiveObjects);
    }
}
