using Pixman;

namespace Basin;

/// <summary>
/// Holds the options of one <see cref="IRenderPass.AddMesh"/> draw.
/// </summary>
public readonly record struct MeshRenderOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MeshRenderOptions"/> struct with premultiplied-over blending and no clip.
    /// </summary>
    public MeshRenderOptions()
    {
    }

    /// <summary>
    /// Gets how the mesh combines with the target. The default is <see cref="RenderBlend.PremultipliedOver"/>.
    /// </summary>
    public RenderBlend Blend { get; init; } = RenderBlend.PremultipliedOver;

    /// <summary>
    /// Gets the region in target pixels that limits the draw, or <see langword="null"/> for no clip.
    /// </summary>
    public PixmanRegion32? Clip { get; init; } = null;
}
