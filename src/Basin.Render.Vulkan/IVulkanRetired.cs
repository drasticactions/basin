namespace Basin.Render.Vulkan;

internal interface IVulkanRetired
{
    ulong RetiredAt { get; set; }

    void ReleaseNow();
}
