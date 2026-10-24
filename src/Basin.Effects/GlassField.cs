namespace Basin.Effects;

/// <summary>
/// The CPU reference for the signed distance field of the glass shaders. Coordinates are local to a rounded rectangle at the origin.
/// </summary>
public static class GlassField
{
    /// <summary>
    /// Returns the signed distance from a point to a rounded rectangle. The value is negative inside and positive outside.
    /// </summary>
    /// <param name="x">The X coordinate of the point.</param>
    /// <param name="y">The Y coordinate of the point.</param>
    /// <param name="width">The rectangle width.</param>
    /// <param name="height">The rectangle height.</param>
    /// <param name="radius">The corner radius. The method clamps it to half the shorter side.</param>
    /// <returns>The signed distance in the same units as the inputs.</returns>
    public static double Distance(double x, double y, double width, double height, double radius)
    {
        var (qx, qy, r) = Fold(x, y, width, height, radius);
        var outside = Math.Sqrt((Math.Max(qx, 0) * Math.Max(qx, 0)) + (Math.Max(qy, 0) * Math.Max(qy, 0)));
        return Math.Min(Math.Max(qx, qy), 0) + outside - r;
    }

    /// <summary>
    /// Returns the outward unit normal of the nearest edge of a rounded rectangle. In a corner it points away from the corner center.
    /// </summary>
    /// <param name="x">The X coordinate of the point.</param>
    /// <param name="y">The Y coordinate of the point.</param>
    /// <param name="width">The rectangle width.</param>
    /// <param name="height">The rectangle height.</param>
    /// <param name="radius">The corner radius.</param>
    /// <returns>A unit vector.</returns>
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

    /// <summary>
    /// Returns how far the lens moves a sample. The offset points inward and is nonzero only in the bevel band inside the edge.
    /// </summary>
    /// <param name="x">The X coordinate of the point.</param>
    /// <param name="y">The Y coordinate of the point.</param>
    /// <param name="width">The rectangle width.</param>
    /// <param name="height">The rectangle height.</param>
    /// <param name="radius">The corner radius.</param>
    /// <param name="bevel">The width of the band inside the edge.</param>
    /// <param name="lens">The strength, as a fraction of the bevel width at the edge.</param>
    /// <returns>The offset, or zero outside the band or when <paramref name="bevel"/> or <paramref name="lens"/> is not positive.</returns>
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

    /// <summary>
    /// Returns the position where the glass samples the backdrop for a point. The result stays half a pixel inside the clamp box.
    /// </summary>
    /// <param name="x">The X coordinate of the point.</param>
    /// <param name="y">The Y coordinate of the point.</param>
    /// <param name="width">The rectangle width.</param>
    /// <param name="height">The rectangle height.</param>
    /// <param name="radius">The corner radius.</param>
    /// <param name="bevel">The width of the band inside the edge.</param>
    /// <param name="lens">The lens strength.</param>
    /// <param name="clamp">The box that every sample must stay inside.</param>
    /// <returns>The lensed and clamped sample position.</returns>
    public static (double X, double Y) Sample(
        double x, double y, double width, double height, double radius, double bevel, double lens, in Box clamp)
    {
        var (dx, dy) = LensOffset(x, y, width, height, radius, bevel, lens);
        return (
            Math.Clamp(x + dx, clamp.X + 0.5, clamp.Right - 0.5),
            Math.Clamp(y + dy, clamp.Y + 0.5, clamp.Bottom - 0.5));
    }

    /// <summary>
    /// Returns the unit vector toward the light in screen coordinates, where Y points down.
    /// </summary>
    /// <param name="degrees">The light angle. 0 is to the right and 90 is down.</param>
    /// <returns>A unit vector.</returns>
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
