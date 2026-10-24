using System.Runtime.InteropServices;

namespace Basin.Greetd;

/// <summary>
/// Reads and writes the greetd frame header. A frame is a 32-bit length in native byte order, then that many bytes of UTF-8 JSON.
/// </summary>
public static class GreetdFrame
{
    /// <summary>The length of the frame header in bytes.</summary>
    public const int HeaderLength = sizeof(uint);

    /// <summary>The largest payload in bytes that a frame can carry. A larger length is a protocol violation.</summary>
    public const int MaxLength = 64 * 1024;

    /// <summary>
    /// Reads one frame from the start of <paramref name="buffer"/>. It sets <paramref name="payload"/> and <paramref name="consumed"/> only for <see cref="GreetdFrameStatus.Complete"/>.
    /// </summary>
    /// <param name="buffer">The received bytes, starting at a frame boundary.</param>
    /// <param name="payload">The JSON bytes of the frame, a slice of <paramref name="buffer"/>. It is empty unless the frame is complete.</param>
    /// <param name="consumed">The number of bytes that the frame uses, header included. It is zero unless the frame is complete.</param>
    /// <returns>The status of the frame at the start of the buffer.</returns>
    public static GreetdFrameStatus TryRead(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> payload, out int consumed)
    {
        payload = default;
        consumed = 0;
        if (buffer.Length < HeaderLength)
        {
            return GreetdFrameStatus.Incomplete;
        }

        var length = MemoryMarshal.Read<uint>(buffer);
        if (length > MaxLength)
        {
            return GreetdFrameStatus.TooLarge;
        }

        if (buffer.Length - HeaderLength < length)
        {
            return GreetdFrameStatus.Incomplete;
        }

        payload = buffer.Slice(HeaderLength, (int)length);
        consumed = HeaderLength + (int)length;
        return GreetdFrameStatus.Complete;
    }

    /// <summary>
    /// Writes a frame header for a payload of <paramref name="length"/> bytes into the start of <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The span that receives the header. It must hold at least <see cref="HeaderLength"/> bytes.</param>
    /// <param name="length">The payload length in bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    public static void WriteHeader(Span<byte> destination, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var value = (uint)length;
        MemoryMarshal.Write(destination, in value);
    }
}
