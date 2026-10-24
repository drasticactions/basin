using Pixman;

namespace Basin;

/// <summary>
/// Holds the options of one render pass.
/// </summary>
public readonly record struct RenderPassOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RenderPassOptions"/> struct with no fences and no color description.
    /// </summary>
    public RenderPassOptions()
    {
    }

    /// <summary>
    /// Gets a sync_file that the pass must wait for before it reads, or -1 for none. It is borrowed for the <see cref="IRenderer.BeginBufferPass"/> call only.
    /// </summary>
    public int WaitFenceFd { get; init; } = -1;

    /// <summary>
    /// Gets a binary syncobj fd that the renderer signals when the pass completes, or -1 for none.
    /// </summary>
    public int SignalFenceFd { get; init; } = -1;

    /// <summary>
    /// Gets the color description of the output, for a renderer that declares <see cref="ColorTransformCapability.Decomposed"/>. Other renderers ignore it.
    /// </summary>
    public Capabilities.ImageDescription? ColorDescription { get; init; } = null;

    /// <summary>
    /// Gets the table from <see cref="Capabilities.ImageDescription.SdrDefault"/> to the target, for the colors that the compositor draws: rects, clears and untextured meshes.
    /// A renderer that declares <see cref="ColorTransformCapability.Lut3D"/> applies it. Other renderers ignore it.
    /// </summary>
    public IColorLut? SolidColorLut { get; init; } = null;
}
