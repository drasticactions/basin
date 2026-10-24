using Basin.Capabilities;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The optional content that a <see cref="WaypipeChannel"/> accepts. The defaults give an shm-only channel without video.
/// </summary>
public sealed record WaypipeChannelOptions
{
    /// <summary>
    /// Gets a value indicating whether the channel accepts dmabuf buffers. When <see langword="false"/>, the dmabuf global is withheld from the client and an OpenDmabuf message ends the channel.
    /// </summary>
    public bool CarriesDmabuf { get; init; }

    /// <summary>
    /// Gets a value indicating whether the channel accepts a video stream. It has an effect only when <see cref="VideoDecoder"/> is also set.
    /// </summary>
    public bool AcceptsVideo { get; init; }

    /// <summary>
    /// Gets the decoder that turns video packets into pixels, or <see langword="null"/> for no video.
    /// </summary>
    public IVideoDecoder? VideoDecoder { get; init; }
}
