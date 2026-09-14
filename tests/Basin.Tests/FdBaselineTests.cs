using System.Net;
using System.Net.Sockets;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class FdBaselineTests
{
    [Fact]
    public void Writes_to_the_standard_streams_inside_a_test_do_not_read_as_leaked_fds()
    {
        using var host = new CompositorTestHost();
        BasinLog.Error($"a test that reports something to standard error");
        BasinReport.Line($"a test that reports something to standard output");
    }

    [Fact]
    public void The_first_async_socket_operation_inside_a_test_does_not_read_as_a_leaked_fd()
    {
        using var host = new CompositorTestHost();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var buffer = new byte[1];
        var pending = socket.ReceiveAsync(buffer, SocketFlags.None);
        socket.SendTo(buffer, socket.LocalEndPoint!);
        Assert.Equal(1, PortalBusTests.Await(host, pending));
    }
}
