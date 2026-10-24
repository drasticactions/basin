using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

/// <summary>
/// Holds the image that a <see cref="IVulkanBackdropEffect"/> made. The renderer samples it over the bounds of the effect.
/// </summary>
/// <param name="View">The view that the renderer samples. The effect owns it.</param>
/// <param name="Extent">The size of the image behind <paramref name="View"/> in pixels.</param>
/// <param name="Source">The region of the image that maps onto the bounds of the effect, in image pixels.</param>
public readonly record struct VulkanBackdropResult(ImageView View, Extent2D Extent, Box Source)
{
    /// <summary>
    /// Gets the image behind <see cref="View"/> in the general layout, for a host that copies instead of samples. It is a null handle when the effect does not set it.
    /// </summary>
    public Image Image { get; init; }
}
