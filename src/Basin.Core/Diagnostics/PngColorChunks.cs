using Basin.Capabilities;

namespace Basin.Diagnostics;

/// <summary>
/// The color chunks of a PNG file: <c>cICP</c> code points from ITU-T H.273, and optional HDR mastering data.
/// </summary>
/// <param name="Primaries">The H.273 color primaries code.</param>
/// <param name="Transfer">The H.273 transfer characteristics code.</param>
/// <param name="Mastering">The source of the <c>mDCV</c> and <c>cLLI</c> chunks, or <see langword="null"/> to write neither.</param>
public readonly record struct PngColorChunks(byte Primaries, byte Transfer, HdrStaticMetadata? Mastering = null)
{
    /// <summary>
    /// Gets the chunks for sRGB primaries and the sRGB transfer function.
    /// </summary>
    public static PngColorChunks Srgb { get; } = new(1, 13);

    /// <summary>
    /// Converts a Wayland image description to PNG color chunks.
    /// </summary>
    /// <param name="description">The image description. <see langword="null"/> means the SDR default.</param>
    /// <param name="hdr">The mastering data. It is kept only for the PQ and HLG transfer functions.</param>
    /// <returns>The chunks, or <see langword="null"/> when the primaries or the transfer function have no H.273 code.</returns>
    public static PngColorChunks? From(ImageDescription? description, HdrStaticMetadata? hdr)
    {
        description ??= ImageDescription.SdrDefault;
        byte? primaries = description.PrimariesNamed switch
        {
            ColorPrimaries.Srgb => 1,
            ColorPrimaries.PalM => 4,
            ColorPrimaries.Pal => 5,
            ColorPrimaries.Ntsc => 6,
            ColorPrimaries.GenericFilm => 8,
            ColorPrimaries.Bt2020 => 9,
            ColorPrimaries.Cie1931Xyz => 10,
            ColorPrimaries.DciP3 => 11,
            ColorPrimaries.DisplayP3 => 12,
            _ => null,
        };
        byte? transfer = description.TransferNamed switch
        {
            ColorTransferFunction.Bt1886 => 1,
            ColorTransferFunction.Gamma22 or ColorTransferFunction.Srgb or ColorTransferFunction.ExtSrgb => 13,
            ColorTransferFunction.Gamma28 => 5,
            ColorTransferFunction.St240 => 7,
            ColorTransferFunction.ExtLinear => 8,
            ColorTransferFunction.Log100 => 9,
            ColorTransferFunction.Log316 => 10,
            ColorTransferFunction.XvYcc => 11,
            ColorTransferFunction.St2084Pq => 16,
            ColorTransferFunction.St428 => 17,
            ColorTransferFunction.Hlg => 18,
            _ => null,
        };
        if (primaries is not { } p || transfer is not { } t)
        {
            return null;
        }

        return new PngColorChunks(p, t, t is 16 or 18 ? hdr : null);
    }
}
