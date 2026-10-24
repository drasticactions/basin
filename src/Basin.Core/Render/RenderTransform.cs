namespace Basin;

/// <summary>
/// Holds a 3x3 projective matrix over target pixels, in row-major order, applied to column points with a homogeneous divide.
/// </summary>
/// <param name="M11">Row 1, column 1.</param>
/// <param name="M12">Row 1, column 2.</param>
/// <param name="M13">Row 1, column 3, the horizontal translation.</param>
/// <param name="M21">Row 2, column 1.</param>
/// <param name="M22">Row 2, column 2.</param>
/// <param name="M23">Row 2, column 3, the vertical translation.</param>
/// <param name="M31">Row 3, column 1, a perspective term.</param>
/// <param name="M32">Row 3, column 2, a perspective term.</param>
/// <param name="M33">Row 3, column 3, the homogeneous scale.</param>
public readonly record struct RenderTransform(
    double M11, double M12, double M13,
    double M21, double M22, double M23,
    double M31, double M32, double M33)
{
    /// <summary>
    /// The transform that changes nothing.
    /// </summary>
    public static readonly RenderTransform Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    /// <summary>
    /// Gets a value indicating whether every element equals the identity exactly.
    /// </summary>
    public bool IsIdentity =>
        M11 == 1 && M12 == 0 && M13 == 0 &&
        M21 == 0 && M22 == 1 && M23 == 0 &&
        M31 == 0 && M32 == 0 && M33 == 1;

    /// <summary>
    /// Gets a value indicating whether the last row is 0, 0, 1, so the transform has no perspective.
    /// </summary>
    public bool IsAffine => M31 == 0 && M32 == 0 && M33 == 1;

    /// <summary>
    /// Gets a value indicating whether the transform is affine with no shear or rotation and a positive scale on both axes. A translation passes.
    /// </summary>
    public bool IsAxisAlignedScale => IsAffine && M12 == 0 && M21 == 0 && M11 > 0 && M22 > 0;

    /// <summary>
    /// Maps a point through the transform, with the homogeneous divide.
    /// </summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns>The mapped point.</returns>
    public (double X, double Y) Map(double x, double y)
    {
        var w = (M31 * x) + (M32 * y) + M33;
        return (((M11 * x) + (M12 * y) + M13) / w, ((M21 * x) + (M22 * y) + M23) / w);
    }

    /// <summary>
    /// Computes the inverse transform.
    /// </summary>
    /// <param name="inverse">The inverse, or <see cref="Identity"/> when the transform cannot be inverted.</param>
    /// <returns><see langword="false"/> when the determinant is nearly zero, for example for a surface seen edge-on.</returns>
    public bool TryInvert(out RenderTransform inverse)
    {
        var c11 = (M22 * M33) - (M23 * M32);
        var c12 = (M23 * M31) - (M21 * M33);
        var c13 = (M21 * M32) - (M22 * M31);
        var det = (M11 * c11) + (M12 * c12) + (M13 * c13);
        if (Math.Abs(det) < 1e-12)
        {
            inverse = Identity;
            return false;
        }

        var s = 1.0 / det;
        inverse = new RenderTransform(
            c11 * s,
            (((M13 * M32) - (M12 * M33)) * s),
            (((M12 * M23) - (M13 * M22)) * s),
            c12 * s,
            (((M11 * M33) - (M13 * M31)) * s),
            (((M13 * M21) - (M11 * M23)) * s),
            c13 * s,
            (((M12 * M31) - (M11 * M32)) * s),
            (((M11 * M22) - (M12 * M21)) * s));
        return true;
    }

    /// <summary>
    /// Maps the four corners of a box and gets the integer box that contains them.
    /// </summary>
    /// <param name="box">The box to map.</param>
    /// <param name="bounds">The bounding box of the mapped corners, or an empty box on failure.</param>
    /// <returns><see langword="false"/> when a corner falls behind the projection plane.</returns>
    public bool TryMapBounds(in Box box, out Box bounds)
    {
        Span<double> xs = stackalloc double[4] { box.X, box.Right, box.X, box.Right };
        Span<double> ys = stackalloc double[4] { box.Y, box.Y, box.Bottom, box.Bottom };
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var i = 0; i < 4; i++)
        {
            var w = (M31 * xs[i]) + (M32 * ys[i]) + M33;
            if (w < 1e-9)
            {
                bounds = default;
                return false;
            }

            var x = ((M11 * xs[i]) + (M12 * ys[i]) + M13) / w;
            var y = ((M21 * xs[i]) + (M22 * ys[i]) + M23) / w;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        var left = (int)Math.Floor(minX);
        var top = (int)Math.Floor(minY);
        bounds = new Box(left, top, (int)Math.Ceiling(maxX) - left, (int)Math.Ceiling(maxY) - top);
        return true;
    }

    /// <summary>
    /// Multiplies two transforms. The result applies <paramref name="b"/> first, then <paramref name="a"/>.
    /// </summary>
    /// <param name="a">The transform that applies second.</param>
    /// <param name="b">The transform that applies first.</param>
    /// <returns>The product <paramref name="a"/> times <paramref name="b"/>.</returns>
    public static RenderTransform Multiply(in RenderTransform a, in RenderTransform b) => new(
        (a.M11 * b.M11) + (a.M12 * b.M21) + (a.M13 * b.M31),
        (a.M11 * b.M12) + (a.M12 * b.M22) + (a.M13 * b.M32),
        (a.M11 * b.M13) + (a.M12 * b.M23) + (a.M13 * b.M33),
        (a.M21 * b.M11) + (a.M22 * b.M21) + (a.M23 * b.M31),
        (a.M21 * b.M12) + (a.M22 * b.M22) + (a.M23 * b.M32),
        (a.M21 * b.M13) + (a.M22 * b.M23) + (a.M23 * b.M33),
        (a.M31 * b.M11) + (a.M32 * b.M21) + (a.M33 * b.M31),
        (a.M31 * b.M12) + (a.M32 * b.M22) + (a.M33 * b.M32),
        (a.M31 * b.M13) + (a.M32 * b.M23) + (a.M33 * b.M33));

    /// <summary>
    /// Makes a translation.
    /// </summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="y">The vertical offset.</param>
    /// <returns>The translation transform.</returns>
    public static RenderTransform Translation(double x, double y) => new(1, 0, x, 0, 1, y, 0, 0, 1);

    /// <summary>
    /// Makes a scale about the origin.
    /// </summary>
    /// <param name="x">The horizontal factor.</param>
    /// <param name="y">The vertical factor.</param>
    /// <returns>The scale transform.</returns>
    public static RenderTransform Scale(double x, double y) => new(x, 0, 0, 0, y, 0, 0, 0, 1);

    /// <summary>
    /// Makes a rotation about a point. A positive angle turns clockwise on a screen, where y points down.
    /// </summary>
    /// <param name="radians">The angle in radians.</param>
    /// <param name="centerX">The horizontal position of the center.</param>
    /// <param name="centerY">The vertical position of the center.</param>
    /// <returns>The rotation transform.</returns>
    public static RenderTransform RotationAbout(double radians, double centerX, double centerY)
    {
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new RenderTransform(
            cos, -sin, centerX - (cos * centerX) + (sin * centerY),
            sin, cos, centerY - (sin * centerX) - (cos * centerY),
            0, 0, 1);
    }
}
