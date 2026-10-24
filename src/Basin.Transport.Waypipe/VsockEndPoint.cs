using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Basin.Transport.Waypipe;

/// <summary>
/// An AF_VSOCK address: a context id and a port. A listener built from a public constructor binds every context id.
/// </summary>
public sealed class VsockEndPoint : EndPoint
{
    /// <summary>
    /// The context id VMADDR_CID_ANY, which binds every context id.
    /// </summary>
    public const uint AnyCid = 0xFFFFFFFF;

    /// <summary>
    /// The context id of the local loopback transport.
    /// </summary>
    public const uint LocalCid = 1;

    /// <summary>
    /// The context id of the host.
    /// </summary>
    public const uint HostCid = 2;

    /// <summary>
    /// The port VMADDR_PORT_ANY. A public constructor refuses it.
    /// </summary>
    public const uint AnyPort = 0xFFFFFFFF;

    /// <summary>
    /// The Linux address family value of AF_VSOCK.
    /// </summary>
    public const AddressFamily VsockFamily = (AddressFamily)40;

    internal const int SockaddrLength = 16;

    /// <summary>
    /// Initializes a new instance of the <see cref="VsockEndPoint"/> class on <see cref="AnyCid"/>.
    /// </summary>
    /// <param name="port">The port, from 1 through 4294967294.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is 0 or <see cref="AnyPort"/>.</exception>
    public VsockEndPoint(uint port)
        : this(AnyCid, port)
    {
        if (port == 0 || port == AnyPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "a vsock port is 1 through 4294967294");
        }
    }

    internal VsockEndPoint(uint cid, uint port)
    {
        Cid = cid;
        Port = port;
    }

    /// <summary>
    /// Gets the context id of the address.
    /// </summary>
    public uint Cid { get; }

    /// <summary>
    /// Gets the port of the address.
    /// </summary>
    public uint Port { get; }

    /// <summary>
    /// Gets the address family. This value is always <see cref="VsockFamily"/>.
    /// </summary>
    public override AddressFamily AddressFamily => VsockFamily;

    /// <summary>
    /// Writes the 16-byte sockaddr_vm form of the address.
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
    /// Reads an address from its sockaddr_vm form.
    /// </summary>
    /// <param name="socketAddress">The socket address to read.</param>
    /// <returns>A new <see cref="VsockEndPoint"/>.</returns>
    /// <exception cref="ArgumentException">The address is shorter than 16 bytes or is not AF_VSOCK.</exception>
    public override EndPoint Create(SocketAddress socketAddress)
    {
        ArgumentNullException.ThrowIfNull(socketAddress);
        if (socketAddress.Size < SockaddrLength)
        {
            throw new ArgumentException("a sockaddr_vm is 16 bytes", nameof(socketAddress));
        }

        Span<byte> bytes = stackalloc byte[SockaddrLength];
        for (var i = 0; i < SockaddrLength; i++)
        {
            bytes[i] = socketAddress[i];
        }

        return Read(bytes);
    }

    /// <summary>
    /// Formats the address as "vsock:PORT" on <see cref="AnyCid"/>, otherwise as "vsock:CID:PORT".
    /// </summary>
    /// <returns>The address as text.</returns>
    public override string ToString() =>
        Cid == AnyCid
            ? string.Create(CultureInfo.InvariantCulture, $"vsock:{Port}")
            : string.Create(CultureInfo.InvariantCulture, $"vsock:{Cid}:{Port}");

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is VsockEndPoint other && other.Cid == Cid && other.Port == Port;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Cid, Port);

    internal void Write(Span<byte> sockaddr)
    {
        sockaddr[..SockaddrLength].Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(sockaddr, (ushort)VsockFamily);
        BinaryPrimitives.WriteUInt32LittleEndian(sockaddr[4..], Port);
        BinaryPrimitives.WriteUInt32LittleEndian(sockaddr[8..], Cid);
    }

    internal static VsockEndPoint Read(ReadOnlySpan<byte> sockaddr)
    {
        if (BinaryPrimitives.ReadUInt16LittleEndian(sockaddr) != (ushort)VsockFamily)
        {
            throw new ArgumentException("the address is not AF_VSOCK", nameof(sockaddr));
        }

        return new VsockEndPoint(
            BinaryPrimitives.ReadUInt32LittleEndian(sockaddr[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(sockaddr[4..]));
    }
}
