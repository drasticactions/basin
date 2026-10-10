using System.Collections.Concurrent;
using System.Net.Sockets;
using Xunit;

#pragma warning disable CA1416

namespace Basin.Transport.Waypipe.Windows.Tests;

public sealed class HyperVWaypipeListenerTests
{
    [Fact]
    public async Task A_peer_becomes_a_channel_on_the_posting_thread_and_its_end_is_disposed_there()
    {
        Assert.SkipWhen(HyperVFacts.LoopbackReason() is not null, HyperVFacts.LoopbackReason() ?? string.Empty);
        var posts = new PostQueue();
        using var listener = await HyperVWaypipeListener.BindAsync(
            new VsockEndPoint(HyperVFacts.FreshPort()),
            HyperVVm.Loopback,
            posts.Post,
            WaypipeCompression.None,
            cancellation: TestContext.Current.CancellationToken);
        var threads = new List<int>();
        var ended = 0;
        listener.ChannelAccepted += channel =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            channel.Ended += _ => Interlocked.Increment(ref ended);
        };

        Assert.Equal(HyperVEndPoint.Loopback, listener.LocalEndPoint.VmId);
        using (var peer = HyperVFacts.Connect(listener.LocalEndPoint))
        {
            posts.PumpUntil(() => threads.Count == 1);
            Assert.Equal(Environment.CurrentManagedThreadId, Assert.Single(threads));
            Assert.Equal(1, listener.Live);
            Assert.Equal(1, listener.Accepted);
            SendConnectionHeader(peer);
        }

        posts.PumpUntil(() => listener.Live == 0 && Volatile.Read(ref ended) == 1);
        Assert.Equal(1, listener.Accepted);
    }

    [Fact]
    public async Task Dispose_closes_a_live_channel()
    {
        Assert.SkipWhen(HyperVFacts.LoopbackReason() is not null, HyperVFacts.LoopbackReason() ?? string.Empty);
        var posts = new PostQueue();
        var listener = await HyperVWaypipeListener.BindAsync(
            new VsockEndPoint(HyperVFacts.FreshPort()),
            HyperVVm.Loopback,
            posts.Post,
            WaypipeCompression.None,
            cancellation: TestContext.Current.CancellationToken);
        WaypipeChannel? accepted = null;
        listener.ChannelAccepted += channel => accepted = channel;
        using var peer = HyperVFacts.Connect(listener.LocalEndPoint);
        SendConnectionHeader(peer);
        posts.PumpUntil(() => accepted is not null);

        listener.Dispose();
        Assert.Equal(0, listener.Live);
        peer.ReceiveTimeout = 10_000;
        var chunk = new byte[4096];
        while (peer.Receive(chunk) > 0)
        {
        }

        var again = HyperVFacts.LoopbackReason();
        Assert.Null(again);
        Assert.ThrowsAny<SocketException>(() => HyperVFacts.Connect(listener.LocalEndPoint).Dispose());
    }

    [Fact]
    public async Task BindAnyPortAsync_binds_two_different_ports_from_the_dynamic_range()
    {
        Assert.SkipWhen(HyperVFacts.LoopbackReason() is not null, HyperVFacts.LoopbackReason() ?? string.Empty);
        var posts = new PostQueue();
        using var first = await HyperVWaypipeListener.BindAnyPortAsync(HyperVVm.Loopback, posts.Post, cancellation: TestContext.Current.CancellationToken);
        using var second = await HyperVWaypipeListener.BindAnyPortAsync(HyperVVm.Loopback, posts.Post, cancellation: TestContext.Current.CancellationToken);

        Assert.NotEqual(first.LocalEndPoint.VsockPort, second.LocalEndPoint.VsockPort);
        Assert.InRange(first.LocalEndPoint.VsockPort!.Value, 49152u, 65535u);
        Assert.InRange(second.LocalEndPoint.VsockPort!.Value, 49152u, 65535u);
    }

    [Fact]
    public async Task A_taken_port_is_reported_by_name()
    {
        Assert.SkipWhen(HyperVFacts.LoopbackReason() is not null, HyperVFacts.LoopbackReason() ?? string.Empty);
        var posts = new PostQueue();
        var endpoint = new VsockEndPoint(HyperVFacts.FreshPort());
        using var first = await HyperVWaypipeListener.BindAsync(endpoint, HyperVVm.Loopback, posts.Post, cancellation: TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<IOException>(() =>
            HyperVWaypipeListener.BindAsync(endpoint, HyperVVm.Loopback, posts.Post, cancellation: TestContext.Current.CancellationToken));
        Assert.Contains("already taken", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_factory_returns_a_hyperv_listener_on_Windows_and_refuses_tcp()
    {
        Assert.SkipWhen(HyperVFacts.LoopbackReason() is not null, HyperVFacts.LoopbackReason() ?? string.Empty);
        var posts = new PostQueue();
        var host = new WaypipeListenerHost(null!, posts.Post);
        var port = HyperVFacts.FreshPort();
        using var listener = await WaypipeListeners.BindAsync($"vsock:{port}", host, HyperVVm.Loopback, cancellation: TestContext.Current.CancellationToken);
        var hyperV = Assert.IsType<HyperVWaypipeListener>(listener);
        Assert.Equal($"vsock:{port}", listener.LocalEndPoint.ToString());
        Assert.Equal(HyperVEndPoint.Loopback, hyperV.LocalEndPoint.VmId);

        var error = await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            WaypipeListeners.BindAsync("127.0.0.1:9800", host, cancellation: TestContext.Current.CancellationToken));
        Assert.Contains("vsock:PORT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WaypipeListener_names_the_hyperv_listener_for_vsock_on_Windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the Windows wording");
        var error = Assert.Throws<PlatformNotSupportedException>(() => WaypipeListener.Bind(new NoLoop(), new VsockEndPoint(9800)));
        Assert.Contains("HyperVWaypipeListener", error.Message, StringComparison.Ordinal);
    }

    private static void SendConnectionHeader(Socket socket)
    {
        Span<byte> header = stackalloc byte[WaypipeWire.ConnectionHeaderLength];
        WaypipeWire.WriteConnectionHeader(header, WaypipeWire.ProtocolVersion, WaypipeCompression.None);
        socket.Send(header);
    }

    private sealed class PostQueue
    {
        private readonly ConcurrentQueue<Action> _queue = new();

        public void Post(Action action) => _queue.Enqueue(action);

        public void PumpUntil(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 10_000;
            while (true)
            {
                while (_queue.TryDequeue(out var action))
                {
                    action();
                }

                if (condition())
                {
                    return;
                }

                Assert.True(Environment.TickCount64 < deadline, "the condition was never reached while pumping");
                Thread.Sleep(5);
            }
        }
    }

    private sealed class NoLoop : ICompositorEventLoop
    {
        public int Fd => -1;

        public void Dispatch(int timeoutMs)
        {
        }

        public IEventSource AddFd(int fd, FdReadiness events, Action<int, FdReadiness> handler) => throw new NotSupportedException();

        public IEventSource AddTimer(Action handler) => throw new NotSupportedException();

        public IEventSource AddSignal(int signalNumber, Action<int> handler) => throw new NotSupportedException();

        public void AddIdle(Action handler) => throw new NotSupportedException();

        public void DispatchIdle()
        {
        }

        public void DeferDestroy(IDisposable victim) => throw new NotSupportedException();
    }
}
