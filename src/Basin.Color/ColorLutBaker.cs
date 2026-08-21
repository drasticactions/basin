using Basin.Capabilities;
using Lcms2;
using Lcms2.Native;

namespace Basin.Color;

public static class ColorLutBaker
{
    public const int DefaultSize = 33;

    public static bool IsIdentity(ImageDescription source, ImageDescription output) =>
        Chromaticities.From(source) == Chromaticities.From(output) &&
        source.TransferNamed == output.TransferNamed &&
        source.TransferPower == output.TransferPower &&
        source.Luminances == output.Luminances;

    public static ColorLut3D Bake(
        ImageDescription source,
        ImageDescription output,
        int size = DefaultSize,
        ColorRenderIntent intent = ColorRenderIntent.Perceptual)
    {
        var sourceTc = TransferCharacteristics.From(source);
        var outputTc = TransferCharacteristics.From(output);
        var matrix = Colorimetry.GamutMatrix(
            Chromaticities.From(source),
            Chromaticities.From(output),
            adaptWhite: intent != ColorRenderIntent.AbsoluteNoAdaptation);

        var anchor = outputTc.ReferenceLuminance / sourceTc.ReferenceLuminance;
        var peak = sourceTc.MaxLuminance * anchor;
        var mapTones = peak > outputTc.MaxLuminance * 1.001;

        var data = new float[size * size * size * 3];
        var index = 0;
        Span<double> rgb = stackalloc double[3];
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    rgb[0] = sourceTc.Decode(r / (double)(size - 1)) * anchor;
                    rgb[1] = sourceTc.Decode(g / (double)(size - 1)) * anchor;
                    rgb[2] = sourceTc.Decode(b / (double)(size - 1)) * anchor;
                    Convert(rgb, matrix);
                    if (mapTones)
                    {
                        ToneMap(rgb, peak, outputTc.MaxLuminance);
                    }

                    data[index++] = (float)outputTc.Encode(rgb[0]);
                    data[index++] = (float)outputTc.Encode(rgb[1]);
                    data[index++] = (float)outputTc.Encode(rgb[2]);
                }
            }
        }

        return new ColorLut3D(size, data);
    }

    public static ColorLut3D? BakeFromIcc(
        byte[] iccData,
        ImageDescription output,
        int size = DefaultSize,
        ColorRenderIntent intent = ColorRenderIntent.Perceptual)
    {
        if (!Lcms2Support.IsAvailable)
        {
            return null;
        }

        var outputTc = TransferCharacteristics.From(output);
        var chroma = Chromaticities.From(output);
        try
        {
            using var source = IccProfile.FromMemory(iccData);
            using var linear = CreateLinearProfile(chroma);
            using var transform = ColorTransform.Create(
                source, PixelFormat.RgbFloat, linear, PixelFormat.RgbFloat, IccIntent(intent));

            var grid = new float[size * size * size * 3];
            var index = 0;
            for (var b = 0; b < size; b++)
            {
                for (var g = 0; g < size; g++)
                {
                    for (var r = 0; r < size; r++)
                    {
                        grid[index++] = r / (float)(size - 1);
                        grid[index++] = g / (float)(size - 1);
                        grid[index++] = b / (float)(size - 1);
                    }
                }
            }

            var linearRgb = new float[grid.Length];
            transform.Transform<float, float>(grid, linearRgb, size * size * size);

            var data = new float[grid.Length];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (float)outputTc.Encode(Math.Max(0, linearRgb[i]) * outputTc.ReferenceLuminance);
            }

            return new ColorLut3D(size, data);
        }
        catch (Lcms2Exception)
        {
            return null;
        }
    }

    private static RenderingIntent IccIntent(ColorRenderIntent intent) => intent switch
    {
        ColorRenderIntent.Relative or ColorRenderIntent.RelativeBpc => RenderingIntent.RelativeColorimetric,
        ColorRenderIntent.Saturation => RenderingIntent.Saturation,
        ColorRenderIntent.Absolute or ColorRenderIntent.AbsoluteNoAdaptation =>
            RenderingIntent.AbsoluteColorimetric,
        _ => RenderingIntent.Perceptual,
    };

    private static IccProfile CreateLinearProfile(in Chromaticities c)
    {
        var white = new cmsCIExyY { x = c.Wx, y = c.Wy, Y = 1 };
        var primaries = new cmsCIExyYTRIPLE
        {
            Red = new cmsCIExyY { x = c.Rx, y = c.Ry, Y = 1 },
            Green = new cmsCIExyY { x = c.Gx, y = c.Gy, Y = 1 },
            Blue = new cmsCIExyY { x = c.Bx, y = c.By, Y = 1 },
        };
        using var gamma = ToneCurve.BuildGamma(1.0);
        return IccProfile.CreateRgb(white, primaries, [gamma, gamma, gamma]);
    }

    private static void Convert(Span<double> rgb, double[] matrix)
    {
        var r = matrix[0] * rgb[0] + matrix[1] * rgb[1] + matrix[2] * rgb[2];
        var g = matrix[3] * rgb[0] + matrix[4] * rgb[1] + matrix[5] * rgb[2];
        var b = matrix[6] * rgb[0] + matrix[7] * rgb[1] + matrix[8] * rgb[2];
        rgb[0] = Math.Max(0, r);
        rgb[1] = Math.Max(0, g);
        rgb[2] = Math.Max(0, b);
    }

    private static void ToneMap(Span<double> rgb, double sourcePeak, double outputPeak)
    {
        var max = Math.Max(rgb[0], Math.Max(rgb[1], rgb[2]));
        if (max <= 0)
        {
            return;
        }

        var scale = Bt2390(max, sourcePeak, outputPeak) / max;
        rgb[0] *= scale;
        rgb[1] *= scale;
        rgb[2] *= scale;
    }

    private static double Bt2390(double nits, double sourcePeak, double outputPeak)
    {
        var sourceMax = TransferCharacteristics.PqInverseEotf(sourcePeak);
        var targetMax = TransferCharacteristics.PqInverseEotf(outputPeak) / sourceMax;
        var e1 = Math.Min(1, TransferCharacteristics.PqInverseEotf(nits) / sourceMax);
        var knee = Math.Max(0, 1.5 * targetMax - 0.5);
        if (e1 <= knee)
        {
            return nits;
        }

        var t = (e1 - knee) / (1 - knee);
        var t2 = t * t;
        var t3 = t2 * t;
        var e2 = (2 * t3 - 3 * t2 + 1) * knee
            + (t3 - 2 * t2 + t) * (1 - knee)
            + (-2 * t3 + 3 * t2) * targetMax;
        return TransferCharacteristics.PqEotf(e2 * sourceMax);
    }
}
