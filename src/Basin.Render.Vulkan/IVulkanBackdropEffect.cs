using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

/// <summary>
/// Represents a backdrop effect that the Vulkan renderer accepts. It records its passes into the command buffer of the render pass.
/// </summary>
public interface IVulkanBackdropEffect : IBackdropEffect
{
    /// <summary>
    /// Records the effect for one region. The render pass is ended before the call and begins again after it.
    /// </summary>
    /// <param name="context">The device, the command buffer, the backdrop view and the region.</param>
    /// <param name="result">The image that the renderer composites over the region. It is only read when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="result"/> holds an image to composite. <see langword="false"/> leaves the backdrop as drawn.</returns>
    bool Record(in VulkanBackdropContext context, out VulkanBackdropResult result);
}
