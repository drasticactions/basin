using Basin.Capabilities;
using Pixman;

namespace Basin;

public readonly record struct HdrStaticMetadata
{
    public enum Transfer : byte
    {
        Sdr = 0,
        TraditionalHdr = 1,
        Pq = 2,
        Hlg = 3,
    }

    public Transfer Eotf { get; init; }

    public (ushort X, ushort Y) PrimaryRed { get; init; }

    public (ushort X, ushort Y) PrimaryGreen { get; init; }

    public (ushort X, ushort Y) PrimaryBlue { get; init; }

    public (ushort X, ushort Y) WhitePoint { get; init; }

    public ushort MaxMasteringLuminance { get; init; }

    public ushort MinMasteringLuminance { get; init; }

    public ushort MaxContentLightLevel { get; init; }

    public ushort MaxFrameAverageLightLevel { get; init; }

    public static HdrStaticMetadata? FromContent(ImageDescription? description)
    {
        if (description is not { TransferNamed: ColorTransferFunction.St2084Pq } ||
            (description.MasteringLuminance is null && description.MaxCll is null && description.MaxFall is null))
        {
            return null;
        }

        static (ushort, ushort) Point(int x, int y) =>
            ((ushort)Math.Clamp(Math.Round(x / 20.0), 0, 50000), (ushort)Math.Clamp(Math.Round(y / 20.0), 0, 50000));
        var primaries = description.MasteringPrimaries ?? (708000, 292000, 170000, 797000, 131000, 46000, 312700, 329000);
        var mastering = description.MasteringLuminance ?? (0u, 0u);
        return new HdrStaticMetadata
        {
            Eotf = Transfer.Pq,
            PrimaryRed = Point(primaries.Rx, primaries.Ry),
            PrimaryGreen = Point(primaries.Gx, primaries.Gy),
            PrimaryBlue = Point(primaries.Bx, primaries.By),
            WhitePoint = Point(primaries.Wx, primaries.Wy),
            MaxMasteringLuminance = (ushort)Math.Min(mastering.Max, ushort.MaxValue),
            MinMasteringLuminance = (ushort)Math.Min(mastering.Min, ushort.MaxValue),
            MaxContentLightLevel = (ushort)Math.Min(description.MaxCll ?? 0, ushort.MaxValue),
            MaxFrameAverageLightLevel = (ushort)Math.Min(description.MaxFall ?? 0, ushort.MaxValue),
        };
    }
}
