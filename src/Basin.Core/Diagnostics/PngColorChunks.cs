using Basin.Capabilities;

namespace Basin.Diagnostics;

public readonly record struct PngColorChunks(byte Primaries, byte Transfer, HdrStaticMetadata? Mastering = null)
{
    public static PngColorChunks Srgb { get; } = new(1, 13);

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
