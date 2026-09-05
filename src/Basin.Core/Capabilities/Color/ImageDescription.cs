namespace Basin.Capabilities;

public sealed record ImageDescription
{
    private static ulong _identityCounter;

    public ImageDescription() => Identity = ++_identityCounter;

    public ulong Identity { get; }

    public ColorPrimaries? PrimariesNamed { get; init; }

    public (int Rx, int Ry, int Gx, int Gy, int Bx, int By, int Wx, int Wy)? PrimariesCustom { get; init; }

    public ColorTransferFunction? TransferNamed { get; init; }

    public uint? TransferPower { get; init; }

    public (uint Min, uint Max, uint Reference)? Luminances { get; init; }

    public (int Rx, int Ry, int Gx, int Gy, int Bx, int By, int Wx, int Wy)? MasteringPrimaries { get; init; }

    public (uint Min, uint Max)? MasteringLuminance { get; init; }

    public uint? MaxCll { get; init; }

    public uint? MaxFall { get; init; }

    public byte[]? IccData { get; init; }

    public static ImageDescription SdrDefault { get; } = new()
    {
        PrimariesNamed = ColorPrimaries.Srgb,
        TransferNamed = ColorTransferFunction.Gamma22,
    };

    public static IEqualityComparer<ImageDescription> ContentComparer { get; } = new ContentEquality();

    private sealed class ContentEquality : IEqualityComparer<ImageDescription>
    {
        public bool Equals(ImageDescription? x, ImageDescription? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.PrimariesNamed == y.PrimariesNamed
                && x.PrimariesCustom == y.PrimariesCustom
                && x.TransferNamed == y.TransferNamed
                && x.TransferPower == y.TransferPower
                && x.Luminances == y.Luminances
                && x.MasteringPrimaries == y.MasteringPrimaries
                && x.MasteringLuminance == y.MasteringLuminance
                && x.MaxCll == y.MaxCll
                && x.MaxFall == y.MaxFall
                && IccEquals(x.IccData, y.IccData);
        }

        public int GetHashCode(ImageDescription description)
        {
            var hash = default(HashCode);
            hash.Add(description.PrimariesNamed);
            hash.Add(description.PrimariesCustom);
            hash.Add(description.TransferNamed);
            hash.Add(description.TransferPower);
            hash.Add(description.Luminances);
            hash.Add(description.MasteringPrimaries);
            hash.Add(description.MasteringLuminance);
            hash.Add(description.MaxCll);
            hash.Add(description.MaxFall);
            if (description.IccData is { } icc)
            {
                hash.Add(icc.Length);
                hash.AddBytes(icc.AsSpan(0, Math.Min(64, icc.Length)));
            }

            return hash.ToHashCode();
        }

        private static bool IccEquals(byte[]? x, byte[]? y) =>
            x is null ? y is null : y is not null && x.AsSpan().SequenceEqual(y);
    }
}
