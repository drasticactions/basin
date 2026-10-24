using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Holds the settings of a <see cref="WobblyEffect"/>.
/// </summary>
public readonly record struct WobblyOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WobblyOptions"/> struct with the default settings.
    /// </summary>
    public WobblyOptions()
    {
    }

    /// <summary>
    /// Gets the number of mesh cells on each side of the drawn patch, clamped between 1 and 64. The default is 6.
    /// </summary>
    public int GridResolution { get; init; } = 6;

    /// <summary>
    /// Gets the friction of the point masses, clamped between 0.1 and 10. The default is 3.
    /// </summary>
    public double Friction { get; init; } = 3;

    /// <summary>
    /// Gets the spring constant between neighboring masses, clamped between 0.1 and 10. The default is 8.
    /// </summary>
    public double SpringK { get; init; } = 8;
}
