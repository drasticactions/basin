using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

/// <summary>
/// Holds the Vulkan handles of a texture that <see cref="VulkanTextures.TryOpen"/> opened for a foreign pass.
/// </summary>
/// <param name="Image">The image of the texture. The texture owns it.</param>
/// <param name="View">The view to sample. The texture owns it.</param>
/// <param name="Format">The Vulkan format of <paramref name="View"/>.</param>
/// <param name="Layout">The layout that the image is in when the pass reads it.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Premultiplied">A value indicating whether the color is premultiplied by alpha.</param>
/// <param name="HasAlpha">A value indicating whether the alpha channel carries coverage.</param>
/// <param name="Foreign">A value indicating whether the image is an imported dmabuf that the caller must acquire from the foreign queue family before use.</param>
public readonly record struct VulkanTextureView(
    Image Image,
    ImageView View,
    Format Format,
    ImageLayout Layout,
    int Width,
    int Height,
    bool Premultiplied,
    bool HasAlpha,
    bool Foreign);
