namespace Basin.Render.Vulkan;

/// <summary>
/// Represents a frame filter that the Vulkan renderer accepts. It records its passes into the command buffer of the render pass.
/// </summary>
public interface IVulkanFilter : IFrameFilter
{
    /// <summary>
    /// Records the filter that reads the source image and writes the whole target image.
    /// </summary>
    /// <param name="context">The device, the command buffer, the source and target images and the per-frame options.</param>
    /// <returns><see langword="true"/> when the filter wrote the target. On <see langword="false"/> the caller draws the source itself.</returns>
    bool Record(in VulkanFilterContext context);
}
