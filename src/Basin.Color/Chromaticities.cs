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
    /// Reads the primaries of an image description. Custom primaries win over named ones.
    /// </summary>
    /// <param name="description">The description to read. Custom primaries are in millionths, as the protocol sends them.</param>
    /// <returns>The primaries. A named set other than BT.2020, DCI-P3 and Display P3 gives <see cref="Srgb"/>.</returns>
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
            ColorPrimaries.Bt2020 => Bt2020,
            ColorPrimaries.DciP3 => DciP3,
            ColorPrimaries.DisplayP3 => DisplayP3,
            _ => Srgb,
        };
    }
}
