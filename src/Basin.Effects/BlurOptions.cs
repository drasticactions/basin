namespace Basin.Effects;

/// <summary>
/// The settings that a backdrop blur applies to every surface that has no override of its own.
/// </summary>
public readonly record struct BlurOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BlurOptions"/> struct with the KWin defaults.
    /// </summary>
    public BlurOptions()
    {
    }

    /// <summary>
    /// Gets the strength step, from 1 to <see cref="BlurStrength.Steps"/>. The blur clamps values outside that range.
    /// </summary>
    public int Strength { get; init; } = 15;

    /// <summary>
    /// Gets the blur radius in pixels for <see cref="BlurKernel.SmolGaussian"/>. When it is <see langword="null"/>, the radius comes from <see cref="Strength"/>.
    /// </summary>
    public double? Radius { get; init; }

    /// <summary>
    /// Gets the upper bound of the grain values that mask banding. Zero turns the grain off.
    /// </summary>
    public int NoiseStrength { get; init; } = 5;

    /// <summary>
    /// Gets the saturation factor of the color matrix. A value of 1 keeps the colors.
    /// </summary>
    public double Saturation { get; init; } = 1.5;

    /// <summary>
    /// Gets the contrast factor of the color matrix. A value of 1 keeps the contrast.
    /// </summary>
    public double Contrast { get; init; } = 1.0;

    /// <summary>
    /// Gets the size of one grain in device pixels. The blur rounds it to a whole number of at least 1.
    /// </summary>
    public double NoiseScale { get; init; } = 1.0;

    /// <summary>
    /// Gets the kernel for every surface that does not set <see cref="BlurSurfaceOptions.Kernel"/>.
    /// </summary>
    public BlurKernel Kernel { get; init; } = BlurKernel.DualKawase;
}
