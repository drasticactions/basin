using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

/// <summary>
/// Holds what a <see cref="IVulkanBackdropEffect"/> reads when it records one region.
/// </summary>
public readonly struct VulkanBackdropContext
{
    /// <summary>
    /// Gets the device that owns the command buffer and the backdrop.
    /// </summary>
    public required VulkanDevice Device { get; init; }

    /// <summary>
    /// Gets the command buffer to record into. No render pass is active in it during the call.
    /// </summary>
    public required CommandBuffer Commands { get; init; }

    /// <summary>
    /// Gets the view of the pixels already drawn, in the general layout and ready for shader reads.
    /// </summary>
    public required ImageView Backdrop { get; init; }

    /// <summary>
    /// Gets the size of the backdrop image in pixels.
    /// </summary>
    public required Extent2D TargetExtent { get; init; }

    /// <summary>
    /// Gets the region of the effect in target pixels, clipped to the target.
    /// </summary>
    public required Box Bounds { get; init; }

    /// <summary>
    /// Gets the whole box of the node in target pixels, before the clip. It is empty when the caller did not give one.
    /// </summary>
    public Box Extent { get; init; }

    /// <summary>
    /// Gets the triangles of a deformed node, or an empty span for a node that no mesh deforms. It is valid only during the call.
    /// </summary>
    public ReadOnlyMemory<MeshVertex> Mesh { get; init; }

    /// <summary>
    /// Gets the key under which the effect keeps state for this node, or <see langword="null"/>.
    /// </summary>
    public object? Key { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Backdrop"/> decodes sRGB, so that samples are linear light. An effect must encode its output again when it is <see langword="true"/>.
    /// </summary>
    public bool LinearLight { get; init; }
}
