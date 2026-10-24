using Basin.Capabilities;
using Lcms2;
using Lcms2.Native;

namespace Basin.Color;

/// <summary>
/// Splits an SDR output transform into the three CRTC stages: a degamma ramp, a color matrix and a
/// gamma ramp. The matrices are row-major in nine elements.
/// </summary>
public static class KmsColorPipeline
{
    /// <summary>
    /// Reads a matrix-shaper ICC profile as a CTM from linear sRGB to panel RGB and a gamma ramp of the
    /// inverse panel curves.
    /// </summary>
    /// <param name="iccData">The output ICC profile bytes.</param>
    /// <param name="gammaSize">The number of entries in each gamma ramp.</param>
    /// <param name="ctm">The matrix, or an empty array on failure.</param>
    /// <param name="gamma">The per-channel encode ramps, or the default value on failure.</param>
    /// <returns><see langword="false"/> when liblcms2 is absent, the profile is not RGB matrix-shaper, prefers a table, or lacks a colorant or curve tag.</returns>
    public static bool TryExtractMatrixShaper(
        byte[] iccData, int gammaSize, out double[] ctm, out OutputGammaRamps gamma)
    {
        ctm = [];
        gamma = default;
        if (!Lcms2Support.IsAvailable)
        {
            return false;
        }

        try
        {
            using var profile = IccProfile.FromMemory(iccData);
            if (profile.ColorSpace != cmsColorSpaceSignature.cmsSigRgbData ||
                !profile.IsMatrixShaper ||
                profile.IsClut(RenderingIntent.RelativeColorimetric, ProfileDirection.Output))
            {
                return false;
            }

            if (profile.ReadXyz(cmsTagSignature.cmsSigRedColorantTag) is not { } red ||
                profile.ReadXyz(cmsTagSignature.cmsSigGreenColorantTag) is not { } green ||
                profile.ReadXyz(cmsTagSignature.cmsSigBlueColorantTag) is not { } blue ||
                profile.ReadToneCurve(cmsTagSignature.cmsSigRedTRCTag) is not { } redTrc ||
                profile.ReadToneCurve(cmsTagSignature.cmsSigGreenTRCTag) is not { } greenTrc ||
                profile.ReadToneCurve(cmsTagSignature.cmsSigBlueTRCTag) is not { } blueTrc)
            {
                return false;
            }

            double[] colorants =
            [
                red.X, green.X, blue.X,
                red.Y, green.Y, blue.Y,
                red.Z, green.Z, blue.Z,
            ];
            ctm = Colorimetry.Multiply(
                Colorimetry.Invert(colorants), Colorimetry.RgbToXyzD50(Chromaticities.Srgb));

            using var redInverse = redTrc.Reverse(0);
            using var greenInverse = greenTrc.Reverse(0);
            using var blueInverse = blueTrc.Reverse(0);
            gamma = new OutputGammaRamps(new ushort[gammaSize], new ushort[gammaSize], new ushort[gammaSize]);
            for (var i = 0; i < gammaSize; i++)
            {
                var value = (float)(i / (double)(gammaSize - 1));
                gamma.Red[i] = Level(redInverse.Evaluate(value));
                gamma.Green[i] = Level(greenInverse.Evaluate(value));
                gamma.Blue[i] = Level(blueInverse.Evaluate(value));
            }

            return true;
        }
        catch (Lcms2Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Tells whether the three stages can convert between two parametric SDR descriptions without a
    /// tone map.
    /// </summary>
    /// <param name="source">The description of the content.</param>
    /// <param name="output">The description that the output displays.</param>
    /// <returns><see langword="false"/> for ICC data, PQ or HLG, different reference whites, or an output peak below the source peak.</returns>
    public static bool CanExpress(ImageDescription source, ImageDescription output)
    {
        if (source.IccData is not null || output.IccData is not null)
        {
            return false;
        }

        if (IsHighDynamicRange(source) || IsHighDynamicRange(output))
        {
            return false;
        }

        var sourceTc = TransferCharacteristics.From(source);
        var outputTc = TransferCharacteristics.From(output);
        if (Math.Abs(sourceTc.ReferenceLuminance - outputTc.ReferenceLuminance) > 0.001)
        {
            return false;
        }

        return outputTc.MaxLuminance >= sourceTc.MaxLuminance * 0.999;
    }

    /// <summary>
    /// Gets the factor that places SDR content below the output peak when the output has more headroom,
    /// for example under EDR.
    /// </summary>
    /// <param name="source">The description of the content.</param>
    /// <param name="output">The description that the output displays.</param>
    /// <returns>The source peak over the output peak, or 1 when the output is not brighter.</returns>
    public static double HeadroomScale(ImageDescription source, ImageDescription output)
    {
        var sourceTc = TransferCharacteristics.From(source);
        var outputTc = TransferCharacteristics.From(output);
        return outputTc.MaxLuminance > sourceTc.MaxLuminance * 1.001
            ? sourceTc.MaxLuminance / outputTc.MaxLuminance
            : 1.0;
    }

    /// <summary>
    /// Builds the gamut matrix from the source primaries to the output primaries, with white
    /// adaptation.
    /// </summary>
    /// <param name="source">The description of the content.</param>
    /// <param name="output">The description that the output displays.</param>
    /// <returns>A new matrix that the caller owns.</returns>
    public static double[] GamutCtm(ImageDescription source, ImageDescription output) =>
        Colorimetry.GamutMatrix(Chromaticities.From(source), Chromaticities.From(output));

    /// <summary>
    /// Builds degamma ramps that decode the source transfer function into linear values from 0 to 1.
    /// </summary>
    /// <param name="source">The description whose transfer function the ramps decode.</param>
    /// <param name="size">The number of entries in each ramp.</param>
    /// <returns>New ramps with the same curve on every channel.</returns>
    public static OutputGammaRamps DecodeRamps(ImageDescription source, int size)
    {
        var tc = TransferCharacteristics.From(source);
        var ramps = new OutputGammaRamps(new ushort[size], new ushort[size], new ushort[size]);
        for (var i = 0; i < size; i++)
        {
            var value = Level(tc.Decode(i / (double)(size - 1)) / tc.MaxLuminance);
            ramps.Red[i] = value;
            ramps.Green[i] = value;
            ramps.Blue[i] = value;
        }

        return ramps;
    }

    /// <summary>
    /// Builds gamma ramps that encode linear values from 0 to 1 with the output transfer function.
    /// </summary>
    /// <param name="output">The description whose transfer function the ramps encode.</param>
    /// <param name="size">The number of entries in each ramp.</param>
    /// <param name="multipliers">The per-channel scale after encoding, for example for night light, or <see langword="null"/> for none.</param>
    /// <returns>New ramps.</returns>
    public static OutputGammaRamps EncodeRamps(
        ImageDescription output, int size, (double R, double G, double B)? multipliers = null)
    {
        var tc = TransferCharacteristics.From(output);
        var (r, g, b) = multipliers ?? (1.0, 1.0, 1.0);
        var ramps = new OutputGammaRamps(new ushort[size], new ushort[size], new ushort[size]);
        for (var i = 0; i < size; i++)
        {
            var encoded = tc.Encode(i / (double)(size - 1) * tc.MaxLuminance);
            ramps.Red[i] = Level(encoded * r);
            ramps.Green[i] = Level(encoded * g);
            ramps.Blue[i] = Level(encoded * b);
        }

        return ramps;
    }

    private static bool IsHighDynamicRange(ImageDescription description) =>
        description.TransferNamed is ColorTransferFunction.St2084Pq or ColorTransferFunction.Hlg;

    private static ushort Level(double value) =>
        (ushort)Math.Round(Math.Clamp(value, 0.0, 1.0) * ushort.MaxValue);
}
