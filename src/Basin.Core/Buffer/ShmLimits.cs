using Wayland;
using Wayland.Server;
using Wayland.Server.Shm;

namespace Basin;

/// <summary>
/// Per-client caps that the managed <c>wl_shm</c> enforces. A request past a cap is a protocol error.
/// </summary>
public sealed record ShmLimits
{
    /// <summary>Gets the maximum number of pools that one client can hold at a time.</summary>
    public int MaxPools { get; init; } = 64;

    /// <summary>Gets the maximum total declared pool size in bytes for one client. A shrink does not reduce the booked size.</summary>
    public long MaxBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>Gets the maximum number of buffers that one client can hold at a time.</summary>
    public int MaxBuffers { get; init; } = 1024;
}
