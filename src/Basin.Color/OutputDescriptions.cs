using Basin.Capabilities;

namespace Basin.Color;

public static class OutputDescriptions
{
    public static ImageDescription Sdr(
        (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy)? chromaticities)
    {
        if (chromaticities is not { } c)
        {
            return ImageDescription.Srgb;
        }

        return new ImageDescription
        {
            PrimariesCustom = (
                (int)(c.Rx * 1e6), (int)(c.Ry * 1e6),
                (int)(c.Gx * 1e6), (int)(c.Gy * 1e6),
                (int)(c.Bx * 1e6), (int)(c.By * 1e6),
                (int)(c.Wx * 1e6), (int)(c.Wy * 1e6)),
            TransferNamed = ColorTransferFunction.Srgb,
        };
    }

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
