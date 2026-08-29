using Basin.Capabilities;
using Lcms2;
using Lcms2.Native;

namespace Basin.Color;

public static class KmsColorPipeline
{
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

    public static double HeadroomScale(ImageDescription source, ImageDescription output)
    {
        var sourceTc = TransferCharacteristics.From(source);
        var outputTc = TransferCharacteristics.From(output);
        return outputTc.MaxLuminance > sourceTc.MaxLuminance * 1.001
            ? sourceTc.MaxLuminance / outputTc.MaxLuminance
            : 1.0;
    }

    public static double[] GamutCtm(ImageDescription source, ImageDescription output) =>
        Colorimetry.GamutMatrix(Chromaticities.From(source), Chromaticities.From(output));

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
