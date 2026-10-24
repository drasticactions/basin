using Basin.Capabilities;

namespace Basin.Effects;

/// <summary>
/// The settings of one backdrop key. A <see langword="null"/> value keeps the shared value from <see cref="BlurOptions"/>.
/// </summary>
public readonly record struct BlurSurfaceOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BlurSurfaceOptions"/> struct with blur on and no overrides.
    /// </summary>
    public BlurSurfaceOptions()
    {
    }

    /// <summary>
    /// Gets a value indicating whether the key is blurred. With blur off, the key can still ask for contrast.
    /// </summary>
    public bool Blur { get; init; } = true;

    /// <summary>
    /// Gets the strength step for this key, or <see langword="null"/> to use <see cref="BlurOptions.Strength"/>.
    /// </summary>
    public int? Strength { get; init; }

    /// <summary>
    /// Gets the <see cref="BlurKernel.SmolGaussian"/> radius in pixels for this key, or <see langword="null"/> to derive it.
    /// </summary>
    public double? Radius { get; init; }

    /// <summary>
    /// Gets the kernel for this key, or <see langword="null"/> to use <see cref="BlurOptions.Kernel"/>.
    /// </summary>
    public BlurKernel? Kernel { get; init; }

    /// <summary>
    /// Gets the corner radii of the blurred region for this key.
    /// </summary>
    public BlurCorners Corners { get; init; } = default;

    /// <summary>
    /// Gets how much of the blurred result shows over the plain backdrop, from 0 to 1.
    /// </summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>
    /// Gets a value indicating whether the key applies the frost and contrast of <see cref="ContrastParameters"/>.
    /// </summary>
    public bool Contrast { get; init; } = false;

    /// <summary>
    /// Gets the contrast, intensity and saturation that the key applies when <see cref="Contrast"/> is set.
    /// </summary>
    public ContrastParameters ContrastParameters { get; init; } = new(1.0, 1.0, 1.0);
}
