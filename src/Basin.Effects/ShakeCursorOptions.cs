namespace Basin.Effects;

public readonly record struct ShakeCursorOptions
{
    public ShakeCursorOptions()
    {
    }

    public double TimeIntervalMillis { get; init; } = 1000;

    public double Sensitivity { get; init; } = 4;

    public double Magnification { get; init; } = 3;

    public double OverMagnification { get; init; } = 1;

    public double DeflateAfterMillis { get; init; } = 2000;

    public double RampMillis { get; init; } = 200;
}
