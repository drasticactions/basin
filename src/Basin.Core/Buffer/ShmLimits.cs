using Wayland;
using Wayland.Server;
using Wayland.Server.Shm;

namespace Basin;

public sealed record ShmLimits
{
    public int MaxPools { get; init; } = 64;

    public long MaxBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    public int MaxBuffers { get; init; } = 1024;
}
