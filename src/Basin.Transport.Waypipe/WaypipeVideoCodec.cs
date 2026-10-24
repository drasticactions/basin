using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The video codec that a waypipe connection header announces.
/// </summary>
public enum WaypipeVideoCodec
{
    /// <summary>
    /// The peer sends no video.
    /// </summary>
    None,

    /// <summary>
    /// The peer sends VP9 video.
    /// </summary>
    Vp9,

    /// <summary>
    /// The peer sends H.264 video.
    /// </summary>
    H264,

    /// <summary>
    /// The peer sends AV1 video.
    /// </summary>
    Av1,
}
