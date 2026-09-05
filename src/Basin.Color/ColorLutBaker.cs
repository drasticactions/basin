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
        var parameters = ColorTransformParameters.From(source, output, intent);
        var data = new float[size * size * size * 3];
        var index = 0;
        Span<double> rgb = stackalloc double[3];
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    rgb[0] = r / (double)(size - 1);
                    rgb[1] = g / (double)(size - 1);
                    rgb[2] = b / (double)(size - 1);
                    parameters.Apply(rgb);
                    data[index++] = (float)rgb[0];
                    data[index++] = (float)rgb[1];
                    data[index++] = (float)rgb[2];
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
}
