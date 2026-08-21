using Pixman;

namespace Basin;

public readonly record struct MeshRenderOptions
{
    public MeshRenderOptions()
    {
    }

    public RenderBlend Blend { get; init; } = RenderBlend.PremultipliedOver;

    public PixmanRegion32? Clip { get; init; } = null;
}
