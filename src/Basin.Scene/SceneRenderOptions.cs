using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// Controls one full <see cref="Scene.Render(IRenderer, IBuffer, in SceneRenderOptions)"/> pass.
/// </summary>
public readonly record struct SceneRenderOptions
{
    private readonly OutputProjection _projection;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneRenderOptions"/> struct with a transparent background, the default projection and the origin at zero.
    /// </summary>
    public SceneRenderOptions()
    {
    }

    /// <summary>
    /// Gets the color that fills the target before the scene draws.
    /// </summary>
    public RenderColor Background { get; init; }

    /// <summary>
    /// Gets the ratio of target pixels to logical pixels. Setting it keeps the transform and size of <see cref="Projection"/>.
    /// </summary>
    public double Scale
    {
        get => _projection.Scale;
        init => _projection = new OutputProjection(value, _projection.Transform, _projection.Width, _projection.Height);
    }

    /// <summary>
    /// Gets the projection from scene coordinates to target pixels, with scale and transform.
    /// </summary>
    public OutputProjection Projection
    {
        get => _projection;
        init => _projection = value;
    }

    /// <summary>
    /// Gets the color lookup tables to apply per buffer node, or <see langword="null"/> for no color conversion.
    /// </summary>
    public IColorLutTable? Luts { get; init; }

    /// <summary>
    /// Gets the color description of the target, or <see langword="null"/> for the renderer's default.
    /// </summary>
    public Capabilities.ImageDescription? ColorDescription { get; init; }

    /// <summary>
    /// Gets the horizontal scene coordinate that maps to the left edge of the target, in logical pixels.
    /// </summary>
    public int OriginX { get; init; }

    /// <summary>
    /// Gets the vertical scene coordinate that maps to the top edge of the target, in logical pixels.
    /// </summary>
    public int OriginY { get; init; }
}
