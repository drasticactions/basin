using System.Runtime.InteropServices;

namespace Basin.Greetd;

public static class GreetdFrame
{
    public const int HeaderLength = sizeof(uint);

    public const int MaxLength = 64 * 1024;

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

    public static void WriteHeader(Span<byte> destination, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var value = (uint)length;
        MemoryMarshal.Write(destination, in value);
    }
}
