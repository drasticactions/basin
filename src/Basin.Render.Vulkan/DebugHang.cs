using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal static unsafe class DebugHang
{
#if DEBUG
    private static int _remaining =
        int.TryParse(Environment.GetEnvironmentVariable("BASIN_DEBUG_GPU_HANG"), out var passes) && passes > 0 ? passes : 0;
#endif

    public static void Submit(VulkanDevice device)
    {
#if DEBUG
        if (_remaining <= 0 || --_remaining > 0)
        {
            return;
        }

        var vk = device.Api;
        var info = new EventCreateInfo { SType = StructureType.EventCreateInfo };
        if (vk.CreateEvent(device.Device, in info, null, out var never) != Result.Success)
        {
            return;
        }

        var commands = device.BeginCommands();
        vk.CmdWaitEvents(commands, 1, in never, PipelineStageFlags.HostBit, PipelineStageFlags.AllCommandsBit, 0, null, 0, null, 0, null);
        _ = device.Submit(commands);
#else
        _ = device;
#endif
    }
}
