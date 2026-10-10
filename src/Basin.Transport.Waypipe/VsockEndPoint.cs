using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Basin.Transport.Waypipe;

public sealed class VsockEndPoint : EndPoint
{
    public const uint AnyCid = 0xFFFFFFFF;

    public const uint LocalCid = 1;

    public const uint HostCid = 2;

    public const uint AnyPort = 0xFFFFFFFF;

    public const AddressFamily VsockFamily = (AddressFamily)40;

    internal const int SockaddrLength = 16;

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

    public uint Cid { get; }

    public uint Port { get; }

    public override AddressFamily AddressFamily => VsockFamily;

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
            throw new ArgumentException("a sockaddr_vm is 16 bytes", nameof(socketAddress));
        }

        Span<byte> bytes = stackalloc byte[SockaddrLength];
        for (var i = 0; i < SockaddrLength; i++)
        {
            bytes[i] = socketAddress[i];
        }

        return Read(bytes);
    }

    public override string ToString() =>
        Cid == AnyCid
            ? string.Create(CultureInfo.InvariantCulture, $"vsock:{Port}")
            : string.Create(CultureInfo.InvariantCulture, $"vsock:{Cid}:{Port}");

    public override bool Equals(object? obj) => obj is VsockEndPoint other && other.Cid == Cid && other.Port == Port;

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
