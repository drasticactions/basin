using Pixman;
using SkiaSharp;

namespace Basin.Render.Skia;

/// <summary>
/// A texture that a Skia render pass can draw as an <see cref="SKImage"/>. Each Skia backend
/// imports buffers into its own implementation.
/// </summary>
public interface ISkiaTexture : ITexture
{
    /// <summary>
    /// Makes the texture's current contents available as an image for one draw. A successful call must
    /// be paired with <see cref="Release"/>.
    /// </summary>
    /// <param name="image">The image to draw. The texture owns it, and it is valid until <see cref="Release"/>.</param>
    /// <returns><see langword="false"/> when the buffer has no readable contents. Do not call <see cref="Release"/> then.</returns>
    bool Acquire(out SKImage image);

    /// <summary>
    /// Ends the access that a successful <see cref="Acquire"/> started.
    /// </summary>
    void Release();

    /// <summary>
    /// Gets a clamped, linear-sampled shader over the image for a vertex mesh draw. The texture caches
    /// the shader and keeps ownership of it.
    /// </summary>
    /// <param name="image">The image that the last <see cref="Acquire"/> returned.</param>
    /// <returns>The cached shader, or <see langword="null"/> when Skia cannot make one.</returns>
    SKShader? MeshShader(SKImage image);
}
