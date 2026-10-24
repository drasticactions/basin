namespace Basin.Capabilities;

/// <summary>
/// Decodes the packets of one video stream into caller memory.
/// </summary>
public interface IVideoDecodeSession : IDisposable
{
    /// <summary>
    /// Decodes one packet into a pixel buffer in the format that the session was opened with.
    /// </summary>
    /// <param name="packet">The encoded packet.</param>
    /// <param name="destination">The address of the first pixel of the destination. It must stay valid until the frame is written.</param>
    /// <param name="stride">The destination row stride in bytes.</param>
    /// <returns>For a synchronous session, whether a frame was written. For an <see cref="IAsyncVideoDecodeSession"/>, whether the packet was queued.</returns>
    bool Decode(ReadOnlySpan<byte> packet, nint destination, int stride);
}
