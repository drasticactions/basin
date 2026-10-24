using Pixman;

namespace Basin;

/// <summary>
/// Holds the options of one <see cref="IRenderPass.AddTexture"/> draw.
/// </summary>
public readonly record struct TextureRenderOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextureRenderOptions"/> struct with the whole texture, no transform, full opacity and linear filtering.
    /// </summary>
    public TextureRenderOptions()
    {
    }

    /// <summary>
    /// Gets the region of the texture to sample, in buffer pixels with fractions kept. An empty box means the whole texture.
    /// </summary>
    public FBox SrcBox { get; init; } = default;

    /// <summary>
    /// Gets the rectangle in target pixels that the source maps to.
    /// </summary>
    public required Box DstBox { get; init; }

    /// <summary>
    /// Gets the transform that maps points of <see cref="DstBox"/> to where they draw. A transform that cannot be inverted draws nothing.
    /// </summary>
    public RenderTransform Transform { get; init; } = RenderTransform.Identity;

    /// <summary>
    /// Gets the opacity that multiplies the texture, from 0 to 1.
    /// </summary>
    public float Alpha { get; init; } = 1f;

    /// <summary>
    /// Gets the region in target pixels that limits the draw, or <see langword="null"/> for no clip. The clip is not transformed.
    /// </summary>
    public PixmanRegion32? Clip { get; init; } = null;

    /// <summary>
    /// Gets a value indicating whether every sampled pixel is opaque. It is a hint that lets a renderer skip the blend.
    /// </summary>
    public bool Opaque { get; init; } = false;

    /// <summary>
    /// Gets the color lookup table to apply to the texels, or <see langword="null"/> for none.
    /// </summary>
    public IColorLut? Lut { get; init; } = null;

    /// <summary>
    /// Gets the color description of the texture, for a renderer that declares <see cref="ColorTransformCapability.Decomposed"/>. <see langword="null"/> means the default SDR description.
    /// </summary>
    public Capabilities.ImageDescription? ColorDescription { get; init; } = null;

    /// <summary>
    /// Gets a pixel shader that replaces the sampling program, or <see langword="null"/> for the normal draw.
    /// </summary>
    public IPixelShader? Shader { get; init; } = null;

    /// <summary>
    /// Gets the filter for a scaled texture.
    /// </summary>
    public TextureFilter Filter { get; init; } = TextureFilter.Linear;

    /// <summary>
    /// Gets the rounded rectangle that clips the draw, in the pixels of <see cref="DstBox"/>. The clip applies after <see cref="Shader"/>.
    /// </summary>
    public CornerClip Corners { get; init; } = default;

    /// <summary>
    /// Gets the factor that multiplies the color of each texel after <see cref="Shader"/>. 1 leaves the color as it is.
    /// </summary>
    public float Brightness { get; init; } = 1f;

    /// <summary>
    /// Gets the saturation of each texel after <see cref="Shader"/>, from 0 for gray to 1 for the color as it is. The renderer applies it before <see cref="Brightness"/>.
    /// </summary>
    public float Saturation { get; init; } = 1f;
}
