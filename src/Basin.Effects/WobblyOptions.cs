using Basin.Scene;

namespace Basin.Effects;

public readonly record struct WobblyOptions
{
    public WobblyOptions()
    {
    }

    public int GridResolution { get; init; } = 6;

    public double Friction { get; init; } = 3;

    public double SpringK { get; init; } = 8;
}
