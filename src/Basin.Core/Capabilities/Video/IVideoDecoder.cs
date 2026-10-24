namespace Basin.Capabilities;

/// <summary>
/// A platform video decoder that opens one session per stream.
/// </summary>
public interface IVideoDecoder
{
    /// <summary>
    /// Tells whether this decoder can decode a codec on this machine.
    /// </summary>
    /// <param name="codec">The codec to check.</param>
    /// <returns><see langword="true"/> when <see cref="Open"/> can open a session for the codec.</returns>
    bool Supports(VideoCodec codec);

    /// <summary>
    /// Opens a decode session for one stream. The session can be an <see cref="IAsyncVideoDecodeSession"/>.
    /// </summary>
    /// <param name="codec">The codec of the stream.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <param name="format">The pixel format that decoded frames are written in.</param>
    /// <returns>A new session that the caller owns and must dispose.</returns>
    IVideoDecodeSession Open(VideoCodec codec, int width, int height, DrmFormat format);
}
