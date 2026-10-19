namespace Basin.Effects;

public static class GlassField
{
    public static double Distance(double x, double y, double width, double height, double radius)
    {
        var (qx, qy, r) = Fold(x, y, width, height, radius);
        var outside = Math.Sqrt((Math.Max(qx, 0) * Math.Max(qx, 0)) + (Math.Max(qy, 0) * Math.Max(qy, 0)));
        return Math.Min(Math.Max(qx, qy), 0) + outside - r;
    }

    public static (double X, double Y) Normal(double x, double y, double width, double height, double radius)
    {
        var (qx, qy, _) = Fold(x, y, width, height, radius);
        var sx = x < width / 2 ? -1.0 : 1.0;
        var sy = y < height / 2 ? -1.0 : 1.0;
        if (qx > 0 && qy > 0)
        {
            var length = Math.Sqrt((qx * qx) + (qy * qy));
            return (sx * qx / length, sy * qy / length);
        }

        return qx > qy ? (sx, 0) : (0, sy);
    }

    public static (double X, double Y) LensOffset(
        double x, double y, double width, double height, double radius, double bevel, double lens)
    {
        if (bevel <= 0 || lens <= 0)
        {
            return (0, 0);
        }

        var d = Distance(x, y, width, height, radius);
        if (d > 0 || d <= -bevel)
        {
            return (0, 0);
        }

        var t = 1 + (d / bevel);
        var magnitude = lens * bevel * t * t;
        var (nx, ny) = Normal(x, y, width, height, radius);
        return (-nx * magnitude, -ny * magnitude);
    }

    public static (double X, double Y) Sample(
        double x, double y, double width, double height, double radius, double bevel, double lens, in Box clamp)
    {
        var (dx, dy) = LensOffset(x, y, width, height, radius, bevel, lens);
        return (
            Math.Clamp(x + dx, clamp.X + 0.5, clamp.Right - 0.5),
            Math.Clamp(y + dy, clamp.Y + 0.5, clamp.Bottom - 0.5));
    }

    public static (double X, double Y) TowardLight(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return (Math.Cos(radians), Math.Sin(radians));
    }

    private static (double Qx, double Qy, double R) Fold(double x, double y, double width, double height, double radius)
    {
        var ex = width / 2;
        var ey = height / 2;
        var r = Math.Clamp(radius, 0, Math.Min(ex, ey));
        return (Math.Abs(x - ex) - ex + r, Math.Abs(y - ey) - ey + r, r);
    }
}
