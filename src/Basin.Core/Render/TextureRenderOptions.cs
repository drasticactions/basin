using Pixman;

namespace Basin;

public readonly record struct TextureRenderOptions
{
    public TextureRenderOptions()
    {
    }

    public FBox SrcBox { get; init; } = default;

    public required Box DstBox { get; init; }

    public RenderTransform Transform { get; init; } = RenderTransform.Identity;

    public float Alpha { get; init; } = 1f;

    public PixmanRegion32? Clip { get; init; } = null;

    public IColorLut? Lut { get; init; } = null;

    public IPixelShader? Shader { get; init; } = null;
}
