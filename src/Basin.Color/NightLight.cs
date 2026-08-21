using Basin.Capabilities;
namespace Basin.Color;

public static class NightLight
{
    public static (double R, double G, double B) Multipliers(double kelvin)
    {
        var rgb = LocusRgb(kelvin);
        var neutral = LocusRgb(6500);
        double[] scaled = [rgb[0] / neutral[0], rgb[1] / neutral[1], rgb[2] / neutral[2]];
        var max = Math.Max(scaled[0], Math.Max(scaled[1], scaled[2]));
        return (Math.Max(0, scaled[0] / max), Math.Max(0, scaled[1] / max), Math.Max(0, scaled[2] / max));
    }

    private static double[] LocusRgb(double kelvin)
    {
        kelvin = Math.Clamp(kelvin, 1667, 25000);
        var (x, y) = PlanckianXy(kelvin);
        double[] white = [x / y, 1, (1 - x - y) / y];
        return Colorimetry.Apply(Colorimetry.Invert(Colorimetry.RgbToXyz(Chromaticities.Srgb)), white);
    }

    public static void FillGammaRamps(double kelvin, Span<ushort> red, Span<ushort> green, Span<ushort> blue)
    {
        var (r, g, b) = Multipliers(kelvin);
        var last = red.Length - 1;
        for (var i = 0; i < red.Length; i++)
        {
            var v = last == 0 ? 1.0 : i / (double)last;
            red[i] = (ushort)Math.Round(Math.Clamp(v * r, 0, 1) * ushort.MaxValue);
            green[i] = (ushort)Math.Round(Math.Clamp(v * g, 0, 1) * ushort.MaxValue);
            blue[i] = (ushort)Math.Round(Math.Clamp(v * b, 0, 1) * ushort.MaxValue);
        }
    }

    public static double[] Ctm(double kelvin)
    {
        var (r, g, b) = Multipliers(kelvin);
        return [r, 0, 0, 0, g, 0, 0, 0, b];
    }

    public static (double X, double Y) PlanckianXy(double kelvin)
    {
        var t = kelvin;
        var t2 = t * t;
        var t3 = t2 * t;
        double x;
        if (t <= 4000)
        {
            x = -0.2661239e9 / t3 - 0.2343589e6 / t2 + 0.8776956e3 / t + 0.179910;
        }
        else
        {
            x = -3.0258469e9 / t3 + 2.1070379e6 / t2 + 0.2226347e3 / t + 0.240390;
        }

        var x2 = x * x;
        var x3 = x2 * x;
        double y;
        if (t <= 2222)
        {
            y = -1.1063814 * x3 - 1.34811020 * x2 + 2.18555832 * x - 0.20219683;
        }
        else if (t <= 4000)
        {
            y = -0.9549476 * x3 - 1.37418593 * x2 + 2.09137015 * x - 0.16748867;
        }
        else
        {
            y = 3.0817580 * x3 - 5.87338670 * x2 + 3.75112997 * x - 0.37001483;
        }

        return (x, y);
    }
}
