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
}
