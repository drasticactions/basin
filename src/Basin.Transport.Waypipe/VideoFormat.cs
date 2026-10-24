using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The codec value in the slice of a waypipe video stream message.
/// </summary>
public enum VideoFormat
{
    /// <summary>
    /// H.264 video.
    /// </summary>
    H264 = 0,

    /// <summary>
    /// VP9 video.
    /// </summary>
    Vp9 = 1,

    /// <summary>
    /// AV1 video.
    /// </summary>
    Av1 = 2,
}
