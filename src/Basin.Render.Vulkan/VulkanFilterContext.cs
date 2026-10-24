using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

/// <summary>
/// Holds what a <see cref="IVulkanFilter"/> reads when it records one frame.
/// </summary>
public readonly struct VulkanFilterContext
{
    /// <summary>
    /// Gets the device that owns the command buffer and both images.
    /// </summary>
    public required VulkanDevice Device { get; init; }

    /// <summary>
    /// Gets the command buffer to record into. No render pass is active in it during the call.
    /// </summary>
    public required CommandBuffer Commands { get; init; }

    /// <summary>
    /// Gets the composed frame, in the shader-read-only layout.
    /// </summary>
    public required Image Source { get; init; }

    /// <summary>
    /// Gets the Vulkan format of <see cref="Source"/>.
    /// </summary>
    public required Format SourceFormat { get; init; }

    /// <summary>
    /// Gets the size of <see cref="Source"/> in pixels.
    /// </summary>
    public required Extent2D SourceExtent { get; init; }

    /// <summary>
    /// Gets the image to write, in the color-attachment layout.
    /// </summary>
    public required Image Target { get; init; }

    /// <summary>
    /// Gets the Vulkan format of <see cref="Target"/>.
    /// </summary>
    public required Format TargetFormat { get; init; }

    /// <summary>
    /// Gets the size of <see cref="Target"/> in pixels.
    /// </summary>
    public required Extent2D TargetExtent { get; init; }

    /// <summary>
    /// Gets the region of <see cref="Target"/> to write, in target pixels.
    /// </summary>
    public required Box Viewport { get; init; }

    /// <summary>
    /// Gets the per-frame values for the filter.
    /// </summary>
    public required FrameFilterOptions Options { get; init; }
}
