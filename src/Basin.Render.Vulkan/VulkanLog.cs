using Basin.Diagnostics;

namespace Basin.Render.Vulkan;

internal static class VulkanLog
{
    internal static readonly BasinLogger Log = BasinLog.For("vulkan");

    private static readonly HashSet<(DrmFormat Format, ulong Modifier, bool Shm)> Rejected = [];

    internal static bool FirstRejection(DrmFormat format, ulong modifier, bool shm) => Rejected.Add((format, modifier, shm));
}
