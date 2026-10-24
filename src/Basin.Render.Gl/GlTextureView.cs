namespace Basin.Render.Gl;

/// <summary>
/// Holds the GL handles of a texture that <see cref="GlTextures.TryOpen"/> opened for a foreign pass.
/// </summary>
/// <param name="Name">The texture name. The texture owns it.</param>
/// <param name="Target">The GL texture target to bind the name to.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Premultiplied">A value indicating whether the color is premultiplied by alpha.</param>
/// <param name="External">A value indicating whether the name is an external OES texture that a 2D sampler cannot read.</param>
/// <param name="HasAlpha">A value indicating whether the alpha channel carries coverage.</param>
public readonly record struct GlTextureView(
    uint Name, uint Target, int Width, int Height, bool Premultiplied, bool External, bool HasAlpha);
