using System.Buffers.Binary;
using K4os.Compression.LZ4;
using Wayland.Server.Shm;
using ZstdSharp;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The resource limits that one channel enforces on its peer. A peer that goes over a limit ends the channel with a <see cref="WaypipeException"/>.
/// </summary>
public sealed record WaypipeLimits
{
    /// <summary>
    /// Gets the largest size in bytes of one shared buffer or image. The default is 1 GiB.
    /// </summary>
    public int MaxRegionBytes { get; init; } = 1024 * 1024 * 1024;

    /// <summary>
    /// Gets the largest total size in bytes of all live buffers and images on the channel. The default is 4 GiB.
    /// </summary>
    public long MaxTotalRegionBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Gets the largest number of live remote ids, which include buffers, images, pipes and ids that wait for a message. The default is 4096.
    /// </summary>
    public int MaxRemoteIds { get; init; } = 4096;

    /// <summary>
    /// Gets the largest declared length in bytes of one waypipe frame. The default is 256 MiB.
    /// </summary>
    public int MaxFrameBytes { get; init; } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets the largest number of bytes that one pipe can buffer before the compositor reads them. The default is 4 MiB.
    /// </summary>
    public int MaxPipeBytes { get; init; } = 4 * 1024 * 1024;
}
