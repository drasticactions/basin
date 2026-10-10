using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Basin.Transport.Waypipe;

public sealed class HyperVEndPoint : EndPoint
{
    public const AddressFamily HyperVFamily = (AddressFamily)34;

    public const ProtocolType RawProtocol = (ProtocolType)1;

    internal const int SockaddrLength = 36;

    private const int VmIdOffset = 4;
    private const int ServiceIdOffset = 20;

    public static readonly Guid Wildcard = Guid.Empty;

    public static readonly Guid Children = new("90db8b89-0d35-4f79-8ce9-49ea0ac8b7cd");

    public static readonly Guid Loopback = new("e0e16197-dd56-4a10-9195-5ee7a155a838");

    public static readonly Guid Parent = new("a42e7cda-d03f-480c-9cc2-a4de20abb878");

    public static readonly Guid VsockTemplate = new("00000000-facb-11e6-bd58-64006a7986d3");

    public HyperVEndPoint(Guid vmId, Guid serviceId)
    {
        VmId = vmId;
        ServiceId = serviceId;
    }

    public Guid VmId { get; }

    public Guid ServiceId { get; }

    public uint? VsockPort => TryVsockPort(ServiceId, out var port) ? port : null;

    public override AddressFamily AddressFamily => HyperVFamily;

    public static HyperVEndPoint ForVsockPort(Guid vmId, uint port) => new(vmId, VsockServiceId(port));

    public static Guid VsockServiceId(uint port)
    {
        Span<byte> bytes = stackalloc byte[16];
        VsockTemplate.TryWriteBytes(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, port);
        return new Guid(bytes);
    }

    public override SocketAddress Serialize()
    {
        var address = new SocketAddress(AddressFamily.Unspecified, SockaddrLength);
        Span<byte> bytes = stackalloc byte[SockaddrLength];
        Write(bytes);
        for (var i = 0; i < SockaddrLength; i++)
        {
            address[i] = bytes[i];
        }

        return address;
    }

    public override EndPoint Create(SocketAddress socketAddress)
    {
        ArgumentNullException.ThrowIfNull(socketAddress);
        if (socketAddress.Size < SockaddrLength)
        {
            throw new ArgumentException("a SOCKADDR_HV is 36 bytes", nameof(socketAddress));
        }

        Span<byte> bytes = stackalloc byte[SockaddrLength];
        for (var i = 0; i < SockaddrLength; i++)
        {
            bytes[i] = socketAddress[i];
        }

        return Read(bytes);
    }

    public override string ToString() =>
        VsockPort is { } port
            ? string.Create(CultureInfo.InvariantCulture, $"vsock:{port}")
            : string.Create(CultureInfo.InvariantCulture, $"hvsock:{VmId}:{ServiceId}");

    public override bool Equals(object? obj) => obj is HyperVEndPoint other && other.VmId == VmId && other.ServiceId == ServiceId;

    public override int GetHashCode() => HashCode.Combine(VmId, ServiceId);

    internal void Write(Span<byte> sockaddr)
    {
        sockaddr[..SockaddrLength].Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(sockaddr, (ushort)HyperVFamily);
        VmId.TryWriteBytes(sockaddr[VmIdOffset..]);
        ServiceId.TryWriteBytes(sockaddr[ServiceIdOffset..]);
    }

    internal static HyperVEndPoint Read(ReadOnlySpan<byte> sockaddr)
    {
        if (BinaryPrimitives.ReadUInt16LittleEndian(sockaddr) != (ushort)HyperVFamily)
        {
            throw new ArgumentException("the address is not AF_HYPERV", nameof(sockaddr));
        }

        return new HyperVEndPoint(
            new Guid(sockaddr.Slice(VmIdOffset, 16)),
            new Guid(sockaddr.Slice(ServiceIdOffset, 16)));
    }

    private static bool TryVsockPort(Guid serviceId, out uint port)
    {
        Span<byte> service = stackalloc byte[16];
        Span<byte> template = stackalloc byte[16];
        serviceId.TryWriteBytes(service);
        VsockTemplate.TryWriteBytes(template);
        port = BinaryPrimitives.ReadUInt32LittleEndian(service);
        return service[4..].SequenceEqual(template[4..]);
    }
}
