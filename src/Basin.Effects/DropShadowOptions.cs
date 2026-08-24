namespace Basin.Effects;

public readonly record struct DropShadowOptions
{
    public DropShadowOptions()
    {
    }

    public DropShadowLayer Primary { get; init; } = new(0, 0, 48, 0.8);

    public DropShadowLayer Secondary { get; init; } = new(0, -6, 24, 0.2);

    public double OffsetX { get; init; }

    public double OffsetY { get; init; } = 12;

    public double CornerRadius { get; init; } = 5;

    public double Overlap { get; init; } = 3;

    public double Strength { get; init; } = 1;

    public RenderColor Color { get; init; } = RenderColor.Black;
}
