using Pixman;

namespace Basin;

public readonly record struct PixelShaderSource
{
    public PixelShaderSource()
    {
    }

    public string? Glsl { get; init; } = null;

    public string? Sksl { get; init; } = null;

    public ReadOnlyMemory<byte> SpirV { get; init; } = default;

    public bool SamplesTexture { get; init; } = false;
}
