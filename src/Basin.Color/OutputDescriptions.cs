using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// Builds the image descriptions and HDR metadata of an output from its colorimetry.
/// </summary>
public static class OutputDescriptions
{
    /// <summary>
    /// Builds an SDR description with a 2.2 gamma and the given primaries.
    /// </summary>
    /// <param name="chromaticities">The CIE 1931 xy coordinates of the primaries and white point, or <see langword="null"/> for the sRGB default.</param>
    /// <returns>The description. It is <see cref="ImageDescription.SdrDefault"/> when no primaries are given.</returns>
    public static ImageDescription Sdr(
        (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)? chromaticities)
    {
        if (chromaticities is not { } c)
        {
            return ImageDescription.SdrDefault;
        }

        return new ImageDescription
        {
            PrimariesCustom = (
                (int)(c.Rx * 1e6), (int)(c.Ry * 1e6),
                (int)(c.Gx * 1e6), (int)(c.Gy * 1e6),
                (int)(c.Bx * 1e6), (int)(c.By * 1e6),
                (int)(c.Wx * 1e6), (int)(c.Wy * 1e6)),
            TransferNamed = ColorTransferFunction.Gamma22,
        };
    }

    /// <summary>
    /// Builds a BT.2020 PQ description with the given luminances and a 203 nit reference white.
    /// </summary>
    /// <param name="maxLuminance">The peak in nits. 0 or less means 1000.</param>
    /// <param name="minLuminance">The black level in nits. 0 or less means 0.</param>
    /// <returns>A new description.</returns>
    public static ImageDescription Hdr10(double maxLuminance, double minLuminance)
    {
        var max = maxLuminance > 0 ? (uint)maxLuminance : 1000u;
        var min = minLuminance > 0 ? (uint)(minLuminance * 10000) : 0u;
        return new ImageDescription
        {
            PrimariesNamed = ColorPrimaries.Bt2020,
            TransferNamed = ColorTransferFunction.St2084Pq,
            Luminances = (min, max, (uint)TransferCharacteristics.PqReferenceLuminance),
        };
    }

    /// <summary>
    /// Builds the PQ static metadata infoframe for an HDR output description.
    /// </summary>
    /// <param name="description">The HDR description. Its luminances, MaxCLL and MaxFALL fill the light levels.</param>
    /// <param name="displayChromaticities">The CIE 1931 xy coordinates of the primaries and white point of the display, or <see langword="null"/> for BT.2020.</param>
    /// <returns>The metadata in infoframe units.</returns>
    public static HdrStaticMetadata HdrMetadataFor(
        ImageDescription description,
        (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)? displayChromaticities)
    {
        var c = displayChromaticities is { } d
            ? new Chromaticities(d.Rx, d.Ry, d.Gx, d.Gy, d.Bx, d.By, d.Wx, d.Wy)
            : Chromaticities.Bt2020;
        static (ushort, ushort) Point(double x, double y) =>
            ((ushort)Math.Round(x / 0.00002), (ushort)Math.Round(y / 0.00002));
        var max = description.Luminances is { } lum && lum.Max > 0 ? lum.Max : 1000;
        var min = description.Luminances is { } lum2 ? lum2.Min : 0;
        return new HdrStaticMetadata
        {
            Eotf = HdrStaticMetadata.Transfer.Pq,
            PrimaryRed = Point(c.Rx, c.Ry),
            PrimaryGreen = Point(c.Gx, c.Gy),
            PrimaryBlue = Point(c.Bx, c.By),
            WhitePoint = Point(c.Wx, c.Wy),
            MaxMasteringLuminance = (ushort)Math.Min(max, ushort.MaxValue),
            MinMasteringLuminance = (ushort)Math.Min(min, ushort.MaxValue),
            MaxContentLightLevel = (ushort)Math.Min(description.MaxCll ?? max, ushort.MaxValue),
            MaxFrameAverageLightLevel = (ushort)Math.Min(description.MaxFall ?? 0, ushort.MaxValue),
        };
    }
}
