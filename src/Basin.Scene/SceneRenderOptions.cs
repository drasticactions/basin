using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

public readonly record struct SceneRenderOptions
{
    private readonly OutputProjection _projection;

    public SceneRenderOptions()
    {
    }

    public RenderColor Background { get; init; }

    public double Scale
    {
        get => _projection.Scale;
        init => _projection = new OutputProjection(value, _projection.Transform, _projection.Width, _projection.Height);
    }

    public OutputProjection Projection
    {
        get => _projection;
        init => _projection = value;
    }

    public IColorLutTable? Luts { get; init; }

    public Capabilities.ImageDescription? ColorDescription { get; init; }

    public int OriginX { get; init; }

    public int OriginY { get; init; }
}
