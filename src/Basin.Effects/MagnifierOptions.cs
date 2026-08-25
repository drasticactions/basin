namespace Basin.Effects;

public readonly record struct MagnifierOptions
{
    public MagnifierOptions()
    {
    }

    public int Width { get; init; } = 200;

    public int Height { get; init; } = 200;

    public double ZoomFactor { get; init; } = 1.2;

    public double InitialZoom { get; init; } = 1.0;

    public int FrameWidth { get; init; } = 5;

    public double RampMillis { get; init; } = 500;
}
