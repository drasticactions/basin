using Pixman;

namespace Basin;

/// <summary>
/// Holds the options of one <see cref="IRenderPass.AddShader"/> draw.
/// </summary>
public readonly record struct ShaderRenderOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShaderRenderOptions"/> struct with full opacity and no clip.
    /// </summary>
    public ShaderRenderOptions()
    {
    }

    /// <summary>
    /// Gets the box that the shader fills, in target pixels. The shader sees it as its canvas from (0, 0) to <c>u_size</c>.
    /// </summary>
    public required Box DstBox { get; init; }

    /// <summary>
    /// Gets the opacity that multiplies the shader output, from 0 to 1.
    /// </summary>
    public float Alpha { get; init; } = 1f;

    /// <summary>
    /// Gets the region in target pixels that limits the draw, or <see langword="null"/> for no clip.
    /// </summary>
    public PixmanRegion32? Clip { get; init; } = null;
}
