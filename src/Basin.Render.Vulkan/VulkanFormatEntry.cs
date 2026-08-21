using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal readonly record struct VulkanFormatEntry(DrmFormat Drm, Format Vk, Format VkSrgb, bool IsYcbcr, bool LinearContent = false)
{
    public bool HasSrgb => VkSrgb != Format.Undefined;

    public bool NeedsShaderDecode(bool usingMutableSrgb) => !LinearContent && !usingMutableSrgb;
}
