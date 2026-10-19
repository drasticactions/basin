using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

public readonly struct VulkanBackdropContext
{
    public required VulkanDevice Device { get; init; }

    public required CommandBuffer Commands { get; init; }

    public required ImageView Backdrop { get; init; }

    public required Extent2D TargetExtent { get; init; }

    public required Box Bounds { get; init; }

    public Box Extent { get; init; }

    public ReadOnlyMemory<MeshVertex> Mesh { get; init; }

    public object? Key { get; init; }

    public bool LinearLight { get; init; }
}
