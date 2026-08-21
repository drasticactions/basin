using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal readonly record struct VulkanModifierProps(
    uint PlaneCount, uint MaxWidth, uint MaxHeight, bool HasMutableSrgb, bool SupportsDisjoint);
