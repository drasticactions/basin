using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal sealed class VulkanFormatProps
{
    public VulkanFormatEntry Entry;
    public bool ShmSupported;
    public bool ShmHasMutableSrgb;
    public uint ShmMaxWidth;
    public uint ShmMaxHeight;
    public readonly Dictionary<ulong, VulkanModifierProps> TextureModifiers = [];
    public readonly Dictionary<ulong, VulkanModifierProps> RenderModifiers = [];
}
