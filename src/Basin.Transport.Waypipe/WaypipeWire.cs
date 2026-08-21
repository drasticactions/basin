using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

public static class WaypipeWire
{
    public const uint ProtocolVersion = 0x11;

    public const uint MinimumProtocolVersion = 0x10;

    public const int ConnectionHeaderLength = 16;

    private const uint FixedBit = 1u << 7;
    private const uint UnsetBit = 1u << 31;
    private const uint NoDmabufBit = 1u << 2;
    private const uint CompressionMask = 0x7u << 8;
    private const uint VideoMask = 0x7u << 11;
    private const uint NoVideo = 0x1u << 11;

    public static uint Header(WaypipeMessageType type, int length) =>
        ((uint)length << 5) | ((uint)type & 0x1f);

    public static (int Length, WaypipeMessageType Type) ParseHeader(uint header) =>
        ((int)(header >> 5), (WaypipeMessageType)(header & 0x1f));

    public static int Padded(int length) => (length + 3) & ~3;

    public static WaypipeConnectionHeader ParseConnectionHeader(
        ReadOnlySpan<byte> bytes, WaypipeCompression expected)
    {
        if (bytes.Length < ConnectionHeaderLength)
        {
            throw new WaypipeException(
                $"the connection header is {ConnectionHeaderLength} bytes and {bytes.Length} arrived");
        }

        var lead = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if ((lead & FixedBit) == 0 && (lead & UnsetBit) != 0)
        {
            throw new WaypipeException(
                $"connection header 0x{lead:x8} has the fixed bit clear, which is a byte-order mismatch");
        }

        var version = (((lead >> 16) & 0xff) << 4) | ((lead >> 3) & 0xf);
        if (version < MinimumProtocolVersion)
        {
            throw new WaypipeException(
                $"the peer asked for wire version {version}, below the {MinimumProtocolVersion} this transport speaks");
        }

        var compression = (WaypipeCompression)((lead & CompressionMask) >> 8);
        if (compression != expected)
        {
            throw new WaypipeException(
                $"the peer compresses with {compression} and this channel was configured for {expected}; "
                + "compression is a hard match rather than a negotiation");
        }

        var video = (lead & VideoMask) >> 11;
        var videoCodec = video switch
        {
            0 or 1 => WaypipeVideoCodec.None,
            2 => WaypipeVideoCodec.Vp9,
            3 => WaypipeVideoCodec.H264,
            4 => WaypipeVideoCodec.Av1,
            _ => throw new WaypipeException($"the peer offers video mode {video}, which names no codec"),
        };

        return new WaypipeConnectionHeader(
            Math.Min(version, ProtocolVersion), compression, videoCodec, (lead & NoDmabufBit) != 0);
    }

    public static void WriteConnectionHeader(
        Span<byte> bytes,
        uint version,
        WaypipeCompression compression,
        bool refusesDmabuf = true,
        WaypipeVideoCodec video = WaypipeVideoCodec.None)
    {
        if (bytes.Length < ConnectionHeaderLength)
        {
            throw new ArgumentException("the connection header needs 16 bytes", nameof(bytes));
        }

        bytes[..ConnectionHeaderLength].Clear();
        var lead = ((version >> 4) << 16) | ((version & 0xf) << 3) | FixedBit;
        lead |= (uint)compression << 8;
        lead |= video switch
        {
            WaypipeVideoCodec.Vp9 => 0x2u << 11,
            WaypipeVideoCodec.H264 => 0x3u << 11,
            WaypipeVideoCodec.Av1 => 0x4u << 11,
            _ => NoVideo,
        };
        if (refusesDmabuf)
        {
            lead |= NoDmabufBit;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, lead);
    }

    public static int TaggedFdCount(uint header2) => (int)((header2 & 0xf800) >> 11);

    public static uint StripFdTag(uint header2) => header2 & ~0xf800u;

    public static uint TagFdCount(uint header2, int fds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fds, 31);
        return StripFdTag(header2) | ((uint)fds << 11);
    }

    public static int MessageLength(uint header2) => (int)(header2 >> 16);
}
