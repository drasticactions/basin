namespace Basin.Effects;

public readonly record struct MouseClickOptions
{
    public MouseClickOptions()
    {
    }

    public double RingLifeMillis { get; init; } = 300;

    public double RingSize { get; init; } = 20;

    public int RingCount { get; init; } = 2;

    public double LineWidth { get; init; } = 1.0;

    public RenderColor LeftColor { get; init; } = new(1f, 0f, 0f, 1f);

    public RenderColor MiddleColor { get; init; } = new(0f, 1f, 0f, 1f);

    public RenderColor RightColor { get; init; } = new(0f, 0f, 1f, 1f);
}
