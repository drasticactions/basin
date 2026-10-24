using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// The settings of a <see cref="FireEffect"/> burn.
/// </summary>
public readonly record struct FireOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FireOptions"/> struct with the default burn.
    /// </summary>
    public FireOptions()
    {
    }

    /// <summary>
    /// Gets the particle count for a window 400 pixels wide. The effect scales the count with the window width.
    /// </summary>
    public int ParticleCount { get; init; } = 2000;

    /// <summary>
    /// Gets the base radius of one particle sprite in logical pixels. Each particle varies it by up to 20 percent.
    /// </summary>
    public float ParticleSize { get; init; } = 16f;

    /// <summary>
    /// Gets the flame color and the shader tint. The particle path ignores it when <see cref="RandomColor"/> is set.
    /// </summary>
    public RenderColor Color { get; init; } = new(0.698f, 0.137f, 0.012f, 1f);

    /// <summary>
    /// Gets a value indicating whether each particle gets a random color from the seeded generator.
    /// </summary>
    public bool RandomColor { get; init; }

    /// <summary>
    /// Gets the margin in logical pixels that the flame extends past the content box on every side.
    /// </summary>
    public int Padding { get; init; } = 200;

    /// <summary>
    /// Gets the seed of the random generator. A fixed seed gives a reproducible burn.
    /// </summary>
    public ulong Seed { get; init; } = 0x2545F4914F6CDD1DUL;
}
