namespace Basin.Capabilities;

/// <summary>
/// The color adjustment for a <see cref="BackgroundEffects.Contrast"/> region. A value of 1 leaves a channel unchanged.
/// </summary>
/// <param name="Contrast">The contrast factor applied to the backdrop.</param>
/// <param name="Intensity">The brightness factor that multiplies the adjusted color.</param>
/// <param name="Saturation">The saturation factor. 0 gives gray.</param>
/// <param name="Frost">Whether to mix <paramref name="FrostColor"/> into the blurred backdrop.</param>
/// <param name="FrostColor">The frost color as 0xAARRGGBB. The alpha sets how much of it is mixed in.</param>
public readonly record struct ContrastParameters(
    double Contrast,
    double Intensity,
    double Saturation,
    bool Frost = false,
    uint FrostColor = 0);
