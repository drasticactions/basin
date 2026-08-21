namespace Basin.Capabilities;

public interface IVideoDecodeSession : IDisposable
{
    bool Decode(ReadOnlySpan<byte> packet, nint destination, int stride);
}
