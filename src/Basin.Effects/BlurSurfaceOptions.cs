using Basin.Capabilities;

namespace Basin.Effects;

public readonly record struct BlurSurfaceOptions
{
    public BlurSurfaceOptions()
    {
    }

    public bool Blur { get; init; } = true;

    public BlurCorners Corners { get; init; } = default;

    public double Opacity { get; init; } = 1.0;

    public bool Contrast { get; init; } = false;

    public ContrastParameters ContrastParameters { get; init; } = new(1.0, 1.0, 1.0);
}
