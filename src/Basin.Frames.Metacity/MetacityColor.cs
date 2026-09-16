namespace Basin.Frames.Metacity;

public readonly record struct MetacityColor(double R, double G, double B, double A)
{
    public static MetacityColor FromRgb(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0, 1.0);

    public static MetacityColor FromHex(uint rgb) =>
        FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    public MetacityColor Shade(double factor)
    {
        var (h, l, s) = ToHls(R, G, B);
        l = Math.Clamp(l * factor, 0.0, 1.0);
        s = Math.Clamp(s * factor, 0.0, 1.0);
        var (r, g, b) = ToRgb(h, l, s);
        return new MetacityColor(r, g, b, A);
    }

    public MetacityColor Blend(MetacityColor foreground, double alpha) =>
        new(R + (foreground.R - R) * alpha, G + (foreground.G - G) * alpha, B + (foreground.B - B) * alpha, A);

    public MetacityColor Mean(MetacityColor other) =>
        new((R + other.R) / 2, (G + other.G) / 2, (B + other.B) / 2, A);

    public uint ToArgb32() =>
        ((uint)Math.Clamp((int)(A * 255), 0, 255) << 24)
        | ((uint)Math.Clamp((int)(R * 255), 0, 255) << 16)
        | ((uint)Math.Clamp((int)(G * 255), 0, 255) << 8)
        | (uint)Math.Clamp((int)(B * 255), 0, 255);

    private static (double H, double L, double S) ToHls(double red, double green, double blue)
    {
        double max, min;
        if (red > green)
        {
            max = red > blue ? red : blue;
            min = green < blue ? green : blue;
        }
        else
        {
            max = green > blue ? green : blue;
            min = red < blue ? red : blue;
        }

        var l = (max + min) / 2;
        var s = 0.0;
        var h = 0.0;
        if (max != min)
        {
            s = l <= 0.5 ? (max - min) / (max + min) : (max - min) / (2 - max - min);
            var delta = max - min;
            if (red == max)
            {
                h = (green - blue) / delta;
            }
            else if (green == max)
            {
                h = 2 + (blue - red) / delta;
            }
            else if (blue == max)
            {
                h = 4 + (red - green) / delta;
            }

            h *= 60;
            if (h < 0.0)
            {
                h += 360;
            }
        }

        return (h, l, s);
    }

    private static (double R, double G, double B) ToRgb(double h, double l, double s)
    {
        if (s == 0)
        {
            return (l, l, l);
        }

        var m2 = l <= 0.5 ? l * (1 + s) : l + s - l * s;
        var m1 = 2 * l - m2;
        return (Channel(h + 120, m1, m2), Channel(h, m1, m2), Channel(h - 120, m1, m2));
    }

    private static double Channel(double hue, double m1, double m2)
    {
        while (hue > 360)
        {
            hue -= 360;
        }

        while (hue < 0)
        {
            hue += 360;
        }

        if (hue < 60)
        {
            return m1 + (m2 - m1) * hue / 60;
        }

        if (hue < 180)
        {
            return m2;
        }

        if (hue < 240)
        {
            return m1 + (m2 - m1) * (240 - hue) / 60;
        }

        return m1;
    }
}
