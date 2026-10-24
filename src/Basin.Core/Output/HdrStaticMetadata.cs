using Basin.Capabilities;
using Pixman;

namespace Basin;

/// <summary>
/// Holds the CTA-861-G static HDR metadata, type 1, that a DRM connector sends in its <c>HDR_OUTPUT_METADATA</c> infoframe.
/// </summary>
public readonly record struct HdrStaticMetadata
{
    /// <summary>
    /// Identifies the EOTF field of the infoframe. The values are the CTA-861-G codes.
    /// </summary>
    public enum Transfer : byte
    {
        /// <summary>
        /// Traditional gamma with SDR luminance.
        /// </summary>
        Sdr = 0,
        /// <summary>
        /// Traditional gamma with HDR luminance.
        /// </summary>
        TraditionalHdr = 1,
        /// <summary>
        /// SMPTE ST 2084 perceptual quantizer.
        /// </summary>
        Pq = 2,
        /// <summary>
        /// Hybrid log-gamma.
        /// </summary>
        Hlg = 3,
    }

    /// <summary>
    /// Gets the transfer function that the sink must apply.
    /// </summary>
    public Transfer Eotf { get; init; }

    /// <summary>
    /// Gets the red mastering primary as CIE 1931 xy in units of 0.00002.
    /// </summary>
    public (ushort X, ushort Y) PrimaryRed { get; init; }

    /// <summary>
    /// Gets the green mastering primary as CIE 1931 xy in units of 0.00002.
    /// </summary>
    public (ushort X, ushort Y) PrimaryGreen { get; init; }

    /// <summary>
    /// Gets the blue mastering primary as CIE 1931 xy in units of 0.00002.
    /// </summary>
    public (ushort X, ushort Y) PrimaryBlue { get; init; }

    /// <summary>
    /// Gets the mastering white point as CIE 1931 xy in units of 0.00002.
    /// </summary>
    public (ushort X, ushort Y) WhitePoint { get; init; }

    /// <summary>
    /// Gets the maximum luminance of the mastering display in cd/m².
    /// </summary>
    public ushort MaxMasteringLuminance { get; init; }

    /// <summary>
    /// Gets the minimum luminance of the mastering display in units of 0.0001 cd/m².
    /// </summary>
    public ushort MinMasteringLuminance { get; init; }

    /// <summary>
    /// Gets the maximum content light level (MaxCLL) in cd/m², or zero when it is unknown.
    /// </summary>
    public ushort MaxContentLightLevel { get; init; }

    /// <summary>
    /// Gets the maximum frame-average light level (MaxFALL) in cd/m², or zero when it is unknown.
    /// </summary>
    public ushort MaxFrameAverageLightLevel { get; init; }

    /// <summary>
    /// Builds the infoframe for content that a client describes. Missing primaries fall back to BT.2020, and values that do not fit are clamped.
    /// </summary>
    /// <param name="description">The client's image description.</param>
    /// <returns>The metadata, or <see langword="null"/> when the transfer is not PQ or the description has no mastering luminance, MaxCLL or MaxFALL.</returns>
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
