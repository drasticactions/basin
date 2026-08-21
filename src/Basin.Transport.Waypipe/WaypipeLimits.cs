using System.Buffers.Binary;
using K4os.Compression.LZ4;
using Wayland.Server.Shm;
using ZstdSharp;

namespace Basin.Transport.Waypipe;

public sealed record WaypipeLimits
{
    public int MaxRegionBytes { get; init; } = 1024 * 1024 * 1024;

    public long MaxTotalRegionBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    public int MaxRemoteIds { get; init; } = 4096;

    public int MaxFrameBytes { get; init; } = 256 * 1024 * 1024;

    public int MaxPipeBytes { get; init; } = 4 * 1024 * 1024;
}
