namespace Basin.Capabilities;

/// <summary>
/// Describes how the encoded values of an image become light, with the units of <c>wp_color_management_v1</c>. Record equality includes <see cref="Identity"/>, so use <see cref="ContentComparer"/> to compare content.
/// </summary>
public sealed record ImageDescription
{
    private static ulong _identityCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageDescription"/> class with a new <see cref="Identity"/>. The constructor is not thread-safe.
    /// </summary>
    public ImageDescription() => Identity = ++_identityCounter;

    /// <summary>
    /// Gets a number that is unique for each description object in the process. A <c>with</c> expression copies it.
    /// </summary>
    public ulong Identity { get; }

    /// <summary>
    /// Gets the named primaries, or <see langword="null"/> when the description uses custom primaries or ICC data.
    /// </summary>
    public ColorPrimaries? PrimariesNamed { get; init; }

    /// <summary>
    /// Gets the CIE 1931 xy chromaticities of the red, green, blue and white points, multiplied by 1,000,000. It is <see langword="null"/> when the primaries are named.
    /// </summary>
    public (int Rx, int Ry, int Gx, int Gy, int Bx, int By, int Wx, int Wy)? PrimariesCustom { get; init; }

    /// <summary>
    /// Gets the named transfer function, or <see langword="null"/> when the description uses a power curve or ICC data.
    /// </summary>
    public ColorTransferFunction? TransferNamed { get; init; }

    /// <summary>
    /// Gets the exponent of a pure power transfer function, multiplied by 10,000. It is <see langword="null"/> when the transfer function is named.
    /// </summary>
    public uint? TransferPower { get; init; }

    /// <summary>
    /// Gets the primary color volume luminances. <c>Min</c> is in units of 0.0001 cd/m², and <c>Max</c> and <c>Reference</c> are in cd/m². It is <see langword="null"/> for the default of the transfer function.
    /// </summary>
    public (uint Min, uint Max, uint Reference)? Luminances { get; init; }

    /// <summary>
    /// Gets the chromaticities of the mastering display, multiplied by 1,000,000, or <see langword="null"/> when the content does not state them.
    /// </summary>
    public (int Rx, int Ry, int Gx, int Gy, int Bx, int By, int Wx, int Wy)? MasteringPrimaries { get; init; }

    /// <summary>
    /// Gets the luminance range of the mastering display. <c>Min</c> is in units of 0.0001 cd/m² and <c>Max</c> is in cd/m².
    /// </summary>
    public (uint Min, uint Max)? MasteringLuminance { get; init; }

    /// <summary>
    /// Gets the maximum content light level in cd/m², or <see langword="null"/> when it is not known.
    /// </summary>
    public uint? MaxCll { get; init; }

    /// <summary>
    /// Gets the maximum frame-average light level in cd/m², or <see langword="null"/> when it is not known.
    /// </summary>
    public uint? MaxFall { get; init; }

    /// <summary>
    /// Gets the ICC profile bytes for a description that came from a profile, otherwise <see langword="null"/>. The caller must not change the array.
    /// </summary>
    public byte[]? IccData { get; init; }

    /// <summary>
    /// Gets the largest linear signal value that the content can encode. It is 1 for ordinary content and 125 for Windows scRGB.
    /// </summary>
    public double SignalCeiling { get; init; } = 1;

    /// <summary>
    /// Gets the inverse tone mapping that expands SDR sources on this output, or <see langword="null"/> for none. Only an output description uses it.
    /// </summary>
    public InverseToneMapping? InverseToneMapping { get; init; }

    /// <summary>
    /// Gets a value indicating whether content with this source description keeps its reference-white anchor instead of inverse tone mapping.
    /// </summary>
    public bool DeclinesInverseToneMapping { get; init; }

    /// <summary>
    /// Gets a 3D table that applies to the encoded values of each source before the decode, or <see langword="null"/> for none. Only an output description uses it.
    /// </summary>
    public ColorLut3D? Look { get; init; }

    /// <summary>
    /// Gets a value indicating whether a conversion into this description needs a baked table, because it has a look or inverse tone mapping.
    /// </summary>
    public bool NeedsBakedTable => InverseToneMapping is not null || Look is not null;

    /// <summary>
    /// Creates the Windows scRGB description: sRGB primaries, extended linear transfer, reference white at 203 cd/m², and a signal ceiling of 125.
    /// </summary>
    /// <returns>A new description with its own <see cref="Identity"/>.</returns>
    public static ImageDescription WindowsScrgb() => new()
    {
        PrimariesNamed = ColorPrimaries.Srgb,
        TransferNamed = ColorTransferFunction.ExtLinear,
        Luminances = (0, 80, 203),
        SignalCeiling = 125,
    };

    /// <summary>
    /// Gets the shared description of untagged content: sRGB primaries with a 2.2 power curve. It also describes an output that reports no colorimetry.
    /// </summary>
    public static ImageDescription SdrDefault { get; } = new()
    {
        PrimariesNamed = ColorPrimaries.Srgb,
        TransferNamed = ColorTransferFunction.Gamma22,
    };

    /// <summary>
    /// Gets a comparer that compares what two descriptions state and ignores <see cref="Identity"/>. It compares <see cref="Look"/> by reference and ICC data by content.
    /// </summary>
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
                && x.SignalCeiling == y.SignalCeiling
                && x.InverseToneMapping == y.InverseToneMapping
                && x.DeclinesInverseToneMapping == y.DeclinesInverseToneMapping
                && ReferenceEquals(x.Look, y.Look)
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
            hash.Add(description.SignalCeiling);
            hash.Add(description.InverseToneMapping);
            hash.Add(description.DeclinesInverseToneMapping);
            hash.Add(description.Look);
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
