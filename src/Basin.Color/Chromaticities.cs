using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// The CIE 1931 xy coordinates of three RGB primaries and a white point.
/// </summary>
/// <param name="Rx">The x coordinate of the red primary.</param>
/// <param name="Ry">The y coordinate of the red primary.</param>
/// <param name="Gx">The x coordinate of the green primary.</param>
/// <param name="Gy">The y coordinate of the green primary.</param>
/// <param name="Bx">The x coordinate of the blue primary.</param>
/// <param name="By">The y coordinate of the blue primary.</param>
/// <param name="Wx">The x coordinate of the white point.</param>
/// <param name="Wy">The y coordinate of the white point.</param>
public readonly record struct Chromaticities(
    double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)
{
    /// <summary>
    /// The sRGB and BT.709 primaries with a D65 white point.
    /// </summary>
    public static readonly Chromaticities Srgb = new(0.64, 0.33, 0.30, 0.60, 0.15, 0.06, 0.3127, 0.3290);

    /// <summary>
    /// The BT.2020 primaries with a D65 white point.
    /// </summary>
    public static readonly Chromaticities Bt2020 = new(0.708, 0.292, 0.170, 0.797, 0.131, 0.046, 0.3127, 0.3290);

    /// <summary>
    /// The DCI-P3 primaries with the DCI white point.
    /// </summary>
    public static readonly Chromaticities DciP3 = new(0.680, 0.320, 0.265, 0.690, 0.150, 0.060, 0.314, 0.351);

    /// <summary>
    /// The DCI-P3 primaries with a D65 white point.
    /// </summary>
    public static readonly Chromaticities DisplayP3 = new(0.680, 0.320, 0.265, 0.690, 0.150, 0.060, 0.3127, 0.3290);

    /// <summary>
    /// The BT.470 System M primaries with illuminant C.
    /// </summary>
    public static readonly Chromaticities PalM = new(0.67, 0.33, 0.21, 0.71, 0.14, 0.08, 0.3101, 0.3162);

    /// <summary>
    /// The BT.601 625-line primaries with a D65 white point.
    /// </summary>
    public static readonly Chromaticities Pal = new(0.64, 0.33, 0.29, 0.60, 0.15, 0.06, 0.3127, 0.3290);

    /// <summary>
    /// The SMPTE 170M primaries with a D65 white point.
    /// </summary>
    public static readonly Chromaticities Ntsc = new(0.630, 0.340, 0.310, 0.595, 0.155, 0.070, 0.3127, 0.3290);

    /// <summary>
    /// The generic film primaries with illuminant C.
    /// </summary>
    public static readonly Chromaticities GenericFilm = new(0.681, 0.319, 0.243, 0.692, 0.145, 0.049, 0.3101, 0.3162);

    /// <summary>
    /// The CIE 1931 XYZ axes as primaries with the equal-energy white point E.
    /// </summary>
    public static readonly Chromaticities Cie1931Xyz = new(1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0 / 3.0, 1.0 / 3.0);

    /// <summary>
    /// The Adobe RGB primaries with a D65 white point.
    /// </summary>
    public static readonly Chromaticities AdobeRgb = new(0.64, 0.33, 0.21, 0.71, 0.15, 0.06, 0.3127, 0.3290);

    /// <summary>
    /// Reads the primaries of an image description. Custom primaries win over named ones.
    /// </summary>
    /// <param name="description">The description to read. Custom primaries are in millionths, as the protocol sends them.</param>
    /// <returns>The primaries. Each named set gives its own chromaticities, and a description without primaries gives <see cref="Srgb"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The named primaries are not a member of <see cref="ColorPrimaries"/>.</exception>
    public static Chromaticities From(ImageDescription description)
    {
        if (description.PrimariesCustom is { } p)
        {
            return new Chromaticities(
                p.Rx * 1e-6, p.Ry * 1e-6, p.Gx * 1e-6, p.Gy * 1e-6,
                p.Bx * 1e-6, p.By * 1e-6, p.Wx * 1e-6, p.Wy * 1e-6);
        }

        return description.PrimariesNamed switch
        {
            null or ColorPrimaries.Srgb => Srgb,
            ColorPrimaries.PalM => PalM,
            ColorPrimaries.Pal => Pal,
            ColorPrimaries.Ntsc => Ntsc,
            ColorPrimaries.GenericFilm => GenericFilm,
            ColorPrimaries.Bt2020 => Bt2020,
            ColorPrimaries.Cie1931Xyz => Cie1931Xyz,
            ColorPrimaries.DciP3 => DciP3,
            ColorPrimaries.DisplayP3 => DisplayP3,
            ColorPrimaries.AdobeRgb => AdobeRgb,
            { } unknown => throw new ArgumentOutOfRangeException(nameof(description), unknown, "unknown named primaries"),
        };
    }
}
