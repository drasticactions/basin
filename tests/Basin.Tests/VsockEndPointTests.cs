using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Basin.Transport.Waypipe;
using Xunit;

namespace Basin.Tests;

public sealed class VsockEndPointTests
{
    [Theory]
    [InlineData("vsock:9800")]
    [InlineData("VSOCK:9800")]
    [InlineData("Vsock:9800")]
    public void A_vsock_prefix_parses_to_every_cid_at_the_port(string text)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "vsock is Linux only");
        var endpoint = Assert.IsType<VsockEndPoint>(WaypipeEndpoint.ForListening(text));
        Assert.Equal(VsockEndPoint.AnyCid, endpoint.Cid);
        Assert.Equal(9800u, endpoint.Port);
        Assert.Equal((AddressFamily)40, endpoint.AddressFamily);
        Assert.Equal("vsock:9800", endpoint.ToString());
    }

    [Theory]
    [InlineData("vsock:", "names no port")]
    [InlineData("vsock:0", "out of range")]
    [InlineData("vsock:abc", "not a decimal number")]
    [InlineData("vsock:+12", "not a decimal number")]
    [InlineData("vsock:4294967295", "out of range")]
    [InlineData("vsock:4294967296", "not a decimal number")]
    [InlineData("vsock:3:9800", "binds every CID")]
    [InlineData("vsock:s3:9800", "binds every CID")]
    public void A_bad_vsock_endpoint_is_refused_with_its_own_message(string text, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => WaypipeEndpoint.ForListening(text));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_address_and_a_path_parse_as_before()
    {
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 9800), WaypipeEndpoint.ForListening("127.0.0.1:9800"));
        var path = Path.Combine(Path.GetTempPath(), $"basin-wp-parse-{Guid.NewGuid():N}");
        var unix = Assert.IsType<UnixDomainSocketEndPoint>(WaypipeEndpoint.ForListening(path));
        Assert.Equal(path, unix.ToString());
    }

    [Fact]
    public void The_public_constructor_refuses_the_any_port_and_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VsockEndPoint(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VsockEndPoint(VsockEndPoint.AnyPort));
    }

    [Fact]
    public void The_text_round_trips()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "vsock is Linux only");
        var endpoint = new VsockEndPoint(4242);
        Assert.Equal(endpoint, WaypipeEndpoint.ForListening(endpoint.ToString()));
    }

    [Fact]
    public void The_sockaddr_vm_layout_is_byte_exact()
    {
        var endpoint = new VsockEndPoint(3, 0x01020304);
        var address = endpoint.Serialize();
        Assert.Equal(16, address.Size);
        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            bytes[i] = address[i];
        }

        Assert.Equal(40, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
        Assert.Equal(endpoint, endpoint.Create(address));
    }

    [Fact]
    public void One_raw_loopback_round_trip_carries_bytes_both_ways()
    {
        Assert.SkipWhen(VsockFacts.LoopbackReason() is not null, VsockFacts.LoopbackReason() ?? string.Empty);
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var listening = VsockNative.OpenListening(new VsockEndPoint(VsockEndPoint.AnyCid, VsockEndPoint.AnyPort), 1);
        try
        {
            var port = VsockNative.LocalEndPoint(listening, new VsockEndPoint(VsockEndPoint.AnyCid, VsockEndPoint.AnyPort)).Port;
            Assert.NotEqual(VsockEndPoint.AnyPort, port);
            using var dialed = new Socket(new SafeSocketHandle(
                VsockNative.Connect(new VsockEndPoint(VsockEndPoint.LocalCid, port)), ownsHandle: true));
            var accepted = -1;
            var deadline = Environment.TickCount64 + 5000;
            while ((accepted = VsockNative.Accept(listening)) < 0 && Environment.TickCount64 < deadline)
            {
                Thread.Sleep(5);
            }

            Assert.True(accepted >= 0, "the listener never accepted the loopback connection");
            using var stream = new VsockStream(accepted);
            dialed.Send("ping"u8);
            var buffer = new byte[4];
            stream.ReadExactly(buffer);
            Assert.Equal("ping"u8.ToArray(), buffer);
            stream.Write("pong"u8);
            var reply = new byte[4];
            Assert.Equal(4, dialed.Receive(reply));
            Assert.Equal("pong"u8.ToArray(), reply);

            stream.Shutdown();
            Assert.Equal(0, dialed.Receive(reply));
        }
        finally
        {
            _ = VsockNative.Close(listening);
        }
    }
}
