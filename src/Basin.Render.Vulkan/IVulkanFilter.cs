namespace Basin.Render.Vulkan;

public interface IVulkanFilter : IFrameFilter
{
    bool Record(in VulkanFilterContext context);
}
