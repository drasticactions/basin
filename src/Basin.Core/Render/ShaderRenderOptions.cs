using Pixman;

namespace Basin;

public readonly record struct ShaderRenderOptions
{
    public ShaderRenderOptions()
    {
    }

    public required Box DstBox { get; init; }

    public float Alpha { get; init; } = 1f;

    public PixmanRegion32? Clip { get; init; } = null;
}
