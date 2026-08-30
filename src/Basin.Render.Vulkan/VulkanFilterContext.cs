using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

public readonly struct VulkanFilterContext
{
    public required VulkanDevice Device { get; init; }

    public required CommandBuffer Commands { get; init; }

    public required Image Source { get; init; }

    public required Format SourceFormat { get; init; }

    public required Extent2D SourceExtent { get; init; }

    public required Image Target { get; init; }

    public required Format TargetFormat { get; init; }

    public required Extent2D TargetExtent { get; init; }

    public required Box Viewport { get; init; }

    public required FrameFilterOptions Options { get; init; }
}
