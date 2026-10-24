namespace Basin.Capabilities;

/// <summary>
/// The video codecs that an <see cref="IVideoDecoder"/> can be asked for.
/// </summary>
public enum VideoCodec
{
    /// <summary>
    /// H.264, also called AVC.
    /// </summary>
    H264,

    /// <summary>
    /// VP9.
    /// </summary>
    Vp9,

    /// <summary>
    /// AV1.
    /// </summary>
    Av1,
}
