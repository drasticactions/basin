using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Basin.Transport.Waypipe;

/// <summary>
/// A Hyper-V socket address: a VM id and a service id. A Linux guest sees the same connection as vsock.
/// </summary>
public sealed class HyperVEndPoint : EndPoint
{
    /// <summary>
    /// The Windows address family value of AF_HYPERV.
    /// </summary>
    public const AddressFamily HyperVFamily = (AddressFamily)34;

    /// <summary>
    /// The Hyper-V socket protocol value HV_PROTOCOL_RAW.
    /// </summary>
    public const ProtocolType RawProtocol = (ProtocolType)1;

    internal const int SockaddrLength = 36;

    private const int VmIdOffset = 4;
    private const int ServiceIdOffset = 20;

    /// <summary>
    /// The VM id that matches every partition.
    /// </summary>
    public static readonly Guid Wildcard = Guid.Empty;

    /// <summary>
    /// The VM id that matches every child partition of this host.
    /// </summary>
    public static readonly Guid Children = new("90db8b89-0d35-4f79-8ce9-49ea0ac8b7cd");

    /// <summary>
    /// The VM id that addresses this partition itself.
    /// </summary>
    public static readonly Guid Loopback = new("e0e16197-dd56-4a10-9195-5ee7a155a838");

    /// <summary>
    /// The VM id that addresses the parent partition.
    /// </summary>
    public static readonly Guid Parent = new("a42e7cda-d03f-480c-9cc2-a4de20abb878");

    /// <summary>
    /// The service id template for a vsock port. The port goes in the first field.
    /// </summary>
    public static readonly Guid VsockTemplate = new("00000000-facb-11e6-bd58-64006a7986d3");

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperVEndPoint"/> class.
    /// </summary>
    /// <param name="vmId">The VM id, either a VM GUID or one of the wildcard ids.</param>
    /// <param name="serviceId">The service id.</param>
    public HyperVEndPoint(Guid vmId, Guid serviceId)
    {
        VmId = vmId;
        ServiceId = serviceId;
    }

    /// <summary>
    /// Gets the VM id of the address.
    /// </summary>
    public Guid VmId { get; }

    /// <summary>
    /// Gets the service id of the address.
    /// </summary>
    public Guid ServiceId { get; }

    /// <summary>
    /// Gets the vsock port that the service id encodes, or <see langword="null"/> when the service id does not follow <see cref="VsockTemplate"/>.
    /// </summary>
    public uint? VsockPort => TryVsockPort(ServiceId, out var port) ? port : null;

    /// <summary>
    /// Gets the address family. This value is always <see cref="HyperVFamily"/>.
    /// </summary>
    public override AddressFamily AddressFamily => HyperVFamily;

    /// <summary>
    /// Makes the address that a Linux guest reaches as a vsock port.
    /// </summary>
    /// <param name="vmId">The VM id to bind.</param>
    /// <param name="port">The vsock port.</param>
    /// <returns>The address with the service id from <see cref="VsockServiceId"/>.</returns>
    public static HyperVEndPoint ForVsockPort(Guid vmId, uint port) => new(vmId, VsockServiceId(port));

    /// <summary>
    /// Encodes a vsock port as a service id through <see cref="VsockTemplate"/>.
    /// </summary>
    /// <param name="port">The vsock port.</param>
    /// <returns>The service id for the port.</returns>
    public static Guid VsockServiceId(uint port)
    {
        Span<byte> bytes = stackalloc byte[16];
        VsockTemplate.TryWriteBytes(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, port);
        return new Guid(bytes);
    }

    /// <summary>
    /// Writes the 36-byte SOCKADDR_HV form of the address.
    /// </summary>
    /// <returns>A socket address with an unspecified family that holds the bytes.</returns>
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

    /// <summary>
    /// Reads an address from its SOCKADDR_HV form.
    /// </summary>
    /// <param name="socketAddress">The socket address to read.</param>
    /// <returns>A new <see cref="HyperVEndPoint"/>.</returns>
    /// <exception cref="ArgumentException">The address is shorter than 36 bytes or is not AF_HYPERV.</exception>
    public override EndPoint Create(SocketAddress socketAddress)
    {
        ArgumentNullException.ThrowIfNull(socketAddress);
        if (socketAddress.Size < SockaddrLength)
        {
            throw new ArgumentException("The address is not 36 bytes. A SOCKADDR_HV is 36 bytes.", nameof(socketAddress));
        }

        Span<byte> bytes = stackalloc byte[SockaddrLength];
        for (var i = 0; i < SockaddrLength; i++)
        {
            bytes[i] = socketAddress[i];
        }

        return Read(bytes);
    }

    /// <summary>
    /// Formats the address as "vsock:PORT" when the service id encodes a port, otherwise as "hvsock:VMID:SERVICEID".
    /// </summary>
    /// <returns>The address as text.</returns>
    public override string ToString() =>
        VsockPort is { } port
            ? string.Create(CultureInfo.InvariantCulture, $"vsock:{port}")
            : string.Create(CultureInfo.InvariantCulture, $"hvsock:{VmId}:{ServiceId}");

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is HyperVEndPoint other && other.VmId == VmId && other.ServiceId == ServiceId;

    /// <inheritdoc/>
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
            throw new ArgumentException("The address is not AF_HYPERV.", nameof(sockaddr));
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
