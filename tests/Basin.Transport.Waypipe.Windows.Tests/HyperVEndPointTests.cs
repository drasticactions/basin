using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Basin.Transport.Waypipe.Windows.Tests;

public sealed class HyperVEndPointTests
{
    [Fact]
    public void Serialize_writes_the_family_and_both_guids_at_their_offsets()
    {
        var vm = Guid.NewGuid();
        var service = Guid.NewGuid();
        var address = new HyperVEndPoint(vm, service).Serialize();

        Assert.Equal(36, address.Size);
        Assert.Equal(34, address[0] | (address[1] << 8));
        Assert.Equal(0, address[2] | (address[3] << 8));
        Assert.Equal(vm, new Guid(Bytes(address, 4, 16)));
        Assert.Equal(service, new Guid(Bytes(address, 20, 16)));
    }

    [Fact]
    public void Create_reads_back_what_Serialize_wrote()
    {
        var endpoint = HyperVEndPoint.ForVsockPort(HyperVEndPoint.Loopback, 9800);
        var back = Assert.IsType<HyperVEndPoint>(endpoint.Create(endpoint.Serialize()));

        Assert.Equal(endpoint, back);
        Assert.Equal(endpoint.GetHashCode(), back.GetHashCode());
        Assert.Equal(HyperVEndPoint.HyperVFamily, back.AddressFamily);
    }

    [Fact]
    public void Create_refuses_an_address_under_36_bytes()
    {
        var endpoint = new HyperVEndPoint(Guid.Empty, Guid.Empty);
        Assert.Throws<ArgumentException>(() => endpoint.Create(new SocketAddress(AddressFamily.Unspecified, 35)));
    }

    [Theory]
    [InlineData(2761u, "00000ac9-facb-11e6-bd58-64006a7986d3")]
    [InlineData(1u, "00000001-facb-11e6-bd58-64006a7986d3")]
    [InlineData(0xFFFFFFFEu, "fffffffe-facb-11e6-bd58-64006a7986d3")]
    public void A_vsock_port_lands_in_the_first_field_of_the_template(uint port, string service)
    {
        var endpoint = HyperVEndPoint.ForVsockPort(Guid.Empty, port);
        Assert.Equal(Guid.Parse(service), endpoint.ServiceId);
        Assert.Equal(port, endpoint.VsockPort);

        Span<byte> bytes = stackalloc byte[16];
        endpoint.ServiceId.TryWriteBytes(bytes);
        Assert.Equal(port, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
    }

    [Fact]
    public void A_service_outside_the_template_has_no_vsock_port()
    {
        Assert.Null(new HyperVEndPoint(Guid.Empty, Guid.Parse("00000ac9-facb-11e6-bd58-64006a7986d4")).VsockPort);
        Assert.Null(new HyperVEndPoint(Guid.Empty, Guid.NewGuid()).VsockPort);
    }

    [Fact]
    public void ToString_is_the_vsock_string_on_the_template_and_hvsock_otherwise()
    {
        Assert.Equal("vsock:9800", HyperVEndPoint.ForVsockPort(HyperVEndPoint.Children, 9800).ToString());

        var vm = Guid.NewGuid();
        var service = Guid.NewGuid();
        Assert.Equal($"hvsock:{vm}:{service}", new HyperVEndPoint(vm, service).ToString());
    }

    [Fact]
    public void ForListening_returns_a_vsock_endpoint_on_Windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "vsock parses on Linux and Windows only");
        var endpoint = Assert.IsType<VsockEndPoint>(WaypipeEndpoint.ForListening("vsock:9800"));
        Assert.Equal(9800u, endpoint.Port);
        Assert.Equal(VsockEndPoint.AnyCid, endpoint.Cid);
    }

    [Fact]
    public void A_cid_names_hyperv_vm_on_Windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the Windows wording");
        var error = Assert.Throws<ArgumentException>(() => WaypipeEndpoint.ForListening("vsock:2:9800"));
        Assert.Contains("--hyperv-vm", error.Message, StringComparison.Ordinal);
    }

    private static byte[] Bytes(SocketAddress address, int offset, int count)
    {
        var bytes = new byte[count];
        for (var i = 0; i < count; i++)
        {
            bytes[i] = address[offset + i];
        }

        return bytes;
    }
}
