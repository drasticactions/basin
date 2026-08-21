using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

public readonly record struct SceneRenderOptions
{
    private readonly double _scale;

    public SceneRenderOptions()
    {
    }

    public RenderColor Background { get; init; }

    public double Scale
    {
        get => _scale == 0 ? 1.0 : _scale;
        init => _scale = value;
    }

}
