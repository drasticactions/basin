using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

public readonly record struct VulkanBackdropResult(ImageView View, Extent2D Extent, Box Source);
