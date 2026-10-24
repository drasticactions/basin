namespace Basin;

/// <summary>
/// Identifies how a scaled texture is sampled.
/// </summary>
public enum TextureFilter
{
    /// <summary>
    /// Bilinear filtering.
    /// </summary>
    Linear,
    /// <summary>
    /// The nearest texel, which keeps pixel art sharp.
    /// </summary>
    Nearest,
    /// <summary>
    /// Hard texel blocks with edges antialiased over at most a quarter texel, for a non-integer upscale. A renderer that does not support it draws <see cref="Nearest"/>.
    /// </summary>
    Pixel,
}
