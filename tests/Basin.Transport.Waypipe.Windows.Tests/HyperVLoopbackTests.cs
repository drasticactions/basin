using System.Net.Sockets;
using Xunit;

#pragma warning disable CA1416

namespace Basin.Transport.Waypipe.Windows.Tests;

public sealed class HyperVLoopbackTests
{
    [Fact]
    public async Task A_loopback_bind_accepts_asynchronously_and_carries_bytes_through_a_NetworkStream()
    {
        Assert.SkipWhen(HyperVFacts.LoopbackReason() is not null, HyperVFacts.LoopbackReason() ?? string.Empty);
        var local = HyperVEndPoint.ForVsockPort(HyperVEndPoint.Loopback, HyperVFacts.FreshPort());
        using var listener = new Socket(HyperVEndPoint.HyperVFamily, SocketType.Stream, HyperVEndPoint.RawProtocol);
        listener.Bind(local);
        listener.Listen(1);
        Assert.Equal(local, listener.LocalEndPoint);

        var accepting = listener.AcceptAsync(TestContext.Current.CancellationToken).AsTask();
        using var client = HyperVFacts.Connect(local);
        using var accepted = await accepting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(accepted.Connected);
        var remote = Assert.IsType<HyperVEndPoint>(accepted.RemoteEndPoint);
        Assert.Equal(HyperVEndPoint.Loopback, remote.VmId);

        using var stream = new NetworkStream(accepted, ownsSocket: false);
        client.Send("hello"u8);
        var got = new byte[5];
        await stream.ReadExactlyAsync(got, TestContext.Current.CancellationToken);
        Assert.Equal("hello"u8.ToArray(), got);

        await stream.WriteAsync("world"u8.ToArray(), TestContext.Current.CancellationToken);
        var back = new byte[5];
        Assert.Equal(5, client.Receive(back));
        Assert.Equal("world"u8.ToArray(), back);

        accepted.Shutdown(SocketShutdown.Both);
        Assert.Equal(0, client.Receive(back));
    }

    [Fact]
    public void ConnectAsync_is_refused_on_an_unbound_socket_so_the_tests_connect_synchronously()
    {
        Assert.SkipWhen(HyperVFacts.LoopbackReason() is not null, HyperVFacts.LoopbackReason() ?? string.Empty);
        var local = HyperVEndPoint.ForVsockPort(HyperVEndPoint.Loopback, HyperVFacts.FreshPort());
        using var listener = new Socket(HyperVEndPoint.HyperVFamily, SocketType.Stream, HyperVEndPoint.RawProtocol);
        listener.Bind(local);
        listener.Listen(1);
        using var client = new Socket(HyperVEndPoint.HyperVFamily, SocketType.Stream, HyperVEndPoint.RawProtocol);
        var error = Assert.ThrowsAny<SocketException>(() => client.ConnectAsync(local, TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult());
        Assert.Equal(SocketError.InvalidArgument, error.SocketErrorCode);
    }

    [Fact]
    public void A_taken_address_fails_at_listen_rather_than_at_bind()
    {
        Assert.SkipWhen(HyperVFacts.LoopbackReason() is not null, HyperVFacts.LoopbackReason() ?? string.Empty);
        var local = HyperVEndPoint.ForVsockPort(HyperVEndPoint.Loopback, HyperVFacts.FreshPort());
        using var first = new Socket(HyperVEndPoint.HyperVFamily, SocketType.Stream, HyperVEndPoint.RawProtocol);
        first.Bind(local);
        first.Listen(1);
        using var second = new Socket(HyperVEndPoint.HyperVFamily, SocketType.Stream, HyperVEndPoint.RawProtocol);
        second.Bind(local);
        var error = Assert.Throws<SocketException>(() => second.Listen(1));
        Assert.Equal(SocketError.AddressAlreadyInUse, error.SocketErrorCode);
    }
}
