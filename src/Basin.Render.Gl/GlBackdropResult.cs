namespace Basin.Render.Gl;

/// <summary>
/// Holds the texture that a <see cref="IGlBackdropEffect"/> made. The renderer samples it over the bounds of the effect.
/// </summary>
/// <param name="Texture">The name of the 2D texture to sample. The effect owns it. The value 0 means no result.</param>
/// <param name="TextureWidth">The width of the texture in pixels.</param>
/// <param name="TextureHeight">The height of the texture in pixels.</param>
/// <param name="Source">The region of the texture that maps onto the bounds of the effect, in texture pixels.</param>
public readonly record struct GlBackdropResult(uint Texture, int TextureWidth, int TextureHeight, Box Source);
