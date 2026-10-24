using Silk.NET.Vulkan;

namespace Basin.UI.Quill;

/// <summary>
/// A Vulkan texture of a Quill canvas, made by <see cref="QuillVulkanTextures"/>.
/// </summary>
public sealed class QuillVulkanTexture : QuillTexture
{
    internal QuillVulkanTexture(Image image, DeviceMemory memory, ImageView view, int width, int height)
        : base(width, height)
    {
        Image = image;
        Memory = memory;
        View = view;
    }

    /// <summary>
    /// Gets the Vulkan image. The pool frees it after the GPU stops using it.
    /// </summary>
    public Image Image { get; }

    /// <summary>
    /// Gets the view of the image that the shaders sample.
    /// </summary>
    public ImageView View { get; }

    internal DeviceMemory Memory { get; }

    internal bool Initialized { get; set; }

    internal int Stamp { get; set; }

    internal ulong RetiredAt { get; set; }
}
