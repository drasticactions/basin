using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal readonly record struct DescriptorAllocation(DescriptorSet Set, DescriptorPool Pool);
