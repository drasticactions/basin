using Basin.Capabilities;

namespace Basin.Color;

public readonly record struct Chromaticities(
    double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)
{
    public static readonly Chromaticities Srgb = new(0.64, 0.33, 0.30, 0.60, 0.15, 0.06, 0.3127, 0.3290);

    public static readonly Chromaticities Bt2020 = new(0.708, 0.292, 0.170, 0.797, 0.131, 0.046, 0.3127, 0.3290);

    public static readonly Chromaticities DciP3 = new(0.680, 0.320, 0.265, 0.690, 0.150, 0.060, 0.314, 0.351);

    public static readonly Chromaticities DisplayP3 = new(0.680, 0.320, 0.265, 0.690, 0.150, 0.060, 0.3127, 0.3290);

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
