namespace Basin.Effects;

public readonly record struct BlurOptions
{
    public BlurOptions()
    {
    }

    public int Strength { get; init; } = 15;

    public int NoiseStrength { get; init; } = 5;

    public double Saturation { get; init; } = 1.5;

    public double Contrast { get; init; } = 1.0;

    public double NoiseScale { get; init; } = 1.0;
}
