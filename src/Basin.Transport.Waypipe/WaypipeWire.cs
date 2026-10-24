using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

/// <summary>
/// Encodes and decodes the fixed fields of the waypipe wire format.
/// </summary>
public static class WaypipeWire
{
    /// <summary>
    /// The highest wire version that this transport speaks.
    /// </summary>
    public const uint ProtocolVersion = 0x11;

    /// <summary>
    /// The lowest wire version that this transport accepts from a peer.
    /// </summary>
    public const uint MinimumProtocolVersion = 0x10;

    /// <summary>
    /// The length in bytes of the connection header that opens a channel.
    /// </summary>
    public const int ConnectionHeaderLength = 16;

    private const uint FixedBit = 1u << 7;
    private const uint UnsetBit = 1u << 31;
    private const uint NoDmabufBit = 1u << 2;
    private const uint CompressionMask = 0x7u << 8;
    private const uint VideoMask = 0x7u << 11;
    private const uint NoVideo = 0x1u << 11;

    /// <summary>
    /// Encodes a frame header word from a message type and a length.
    /// </summary>
    /// <param name="type">The message type.</param>
    /// <param name="length">The length of the frame in bytes, with the header word and without padding.</param>
    /// <returns>The header word, in host order.</returns>
    public static uint Header(WaypipeMessageType type, int length) =>
        ((uint)length << 5) | ((uint)type & 0x1f);

    /// <summary>
    /// Decodes a frame header word.
    /// </summary>
    /// <param name="header">The header word, in host order.</param>
    /// <returns>The frame length without padding, and the message type.</returns>
    public static (int Length, WaypipeMessageType Type) ParseHeader(uint header) =>
        ((int)(header >> 5), (WaypipeMessageType)(header & 0x1f));

    /// <summary>
    /// Rounds a frame length up to the next multiple of four bytes.
    /// </summary>
    /// <param name="length">The unpadded length in bytes.</param>
    /// <returns>The padded length in bytes.</returns>
    public static int Padded(int length) => (length + 3) & ~3;

    /// <summary>
    /// Reads and checks the connection header that a peer sends first.
    /// </summary>
    /// <param name="bytes">At least <see cref="ConnectionHeaderLength"/> bytes from the start of the stream.</param>
    /// <param name="expected">The compression that this channel uses. The peer must use the same.</param>
    /// <returns>The parsed header, with the version clamped to <see cref="ProtocolVersion"/>.</returns>
    /// <exception cref="WaypipeException">The header is short, has the wrong byte order, has a version below <see cref="MinimumProtocolVersion"/>, a different compression or an unknown video mode.</exception>
    public static WaypipeConnectionHeader ParseConnectionHeader(
        ReadOnlySpan<byte> bytes, WaypipeCompression expected)
    {
        if (bytes.Length < ConnectionHeaderLength)
        {
            throw new WaypipeException(
                $"The connection header must be {ConnectionHeaderLength} bytes, but {bytes.Length} bytes arrived.");
        }

        var lead = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if ((lead & FixedBit) == 0 && (lead & UnsetBit) != 0)
        {
            throw new WaypipeException(
                $"Connection header 0x{lead:x8} has the fixed bit clear. The peer uses a different byte order.");
        }

        var version = (((lead >> 16) & 0xff) << 4) | ((lead >> 3) & 0xf);
        if (version < MinimumProtocolVersion)
        {
            throw new WaypipeException(
                $"The peer asked for wire version {version}. This transport needs version {MinimumProtocolVersion} or later.");
        }

        var compression = (WaypipeCompression)((lead & CompressionMask) >> 8);
        if (compression != expected)
        {
            throw new WaypipeException(
                $"The peer uses {compression} compression, but this channel uses {expected}. Start both sides with the same compression.");
        }

        var video = (lead & VideoMask) >> 11;
        var videoCodec = video switch
        {
            0 or 1 => WaypipeVideoCodec.None,
            2 => WaypipeVideoCodec.Vp9,
            3 => WaypipeVideoCodec.H264,
            4 => WaypipeVideoCodec.Av1,
            _ => throw new WaypipeException($"The peer offers video mode {video}, which is not a known codec."),
        };

        return new WaypipeConnectionHeader(
            Math.Min(version, ProtocolVersion), compression, videoCodec, (lead & NoDmabufBit) != 0);
    }

    /// <summary>
    /// Writes a connection header into the first <see cref="ConnectionHeaderLength"/> bytes of a buffer.
    /// </summary>
    /// <param name="bytes">The destination. It must hold at least <see cref="ConnectionHeaderLength"/> bytes.</param>
    /// <param name="version">The wire version to announce.</param>
    /// <param name="compression">The compression to announce.</param>
    /// <param name="refusesDmabuf">A value that sets the flag that turns off dmabuf transfer.</param>
    /// <param name="video">The video codec to announce, or <see cref="WaypipeVideoCodec.None"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is shorter than <see cref="ConnectionHeaderLength"/>.</exception>
    public static void WriteConnectionHeader(
        Span<byte> bytes,
        uint version,
        WaypipeCompression compression,
        bool refusesDmabuf = true,
        WaypipeVideoCodec video = WaypipeVideoCodec.None)
    {
        if (bytes.Length < ConnectionHeaderLength)
        {
            throw new ArgumentException("The connection header needs 16 bytes.", nameof(bytes));
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

    /// <summary>
    /// Reads the descriptor count that waypipe tags into the second word of a Wayland message.
    /// </summary>
    /// <param name="header2">The second header word of a Wayland message, which holds the size and the opcode.</param>
    /// <returns>The number of descriptors that the message carries, from 0 through 31.</returns>
    public static int TaggedFdCount(uint header2) => (int)((header2 & 0xf800) >> 11);

    /// <summary>
    /// Removes the descriptor count tag from the second word of a Wayland message.
    /// </summary>
    /// <param name="header2">The tagged second header word.</param>
    /// <returns>The header word as libwayland expects it.</returns>
    public static uint StripFdTag(uint header2) => header2 & ~0xf800u;

    /// <summary>
    /// Writes a descriptor count tag into the second word of a Wayland message and replaces any earlier tag.
    /// </summary>
    /// <param name="header2">The second header word of a Wayland message.</param>
    /// <param name="fds">The number of descriptors, from 0 through 31.</param>
    /// <returns>The tagged header word.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fds"/> is negative or greater than 31.</exception>
    public static uint TagFdCount(uint header2, int fds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fds, 31);
        return StripFdTag(header2) | ((uint)fds << 11);
    }

    /// <summary>
    /// Reads the message size from the second word of a Wayland message.
    /// </summary>
    /// <param name="header2">The second header word of a Wayland message.</param>
    /// <returns>The size of the message in bytes, with its header.</returns>
    public static int MessageLength(uint header2) => (int)(header2 >> 16);
}
