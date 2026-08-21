using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

public interface IVulkanBackdropEffect : IBackdropEffect
{
    bool Record(in VulkanBackdropContext context, out VulkanBackdropResult result);
}
