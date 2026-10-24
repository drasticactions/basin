namespace Basin.Effects;

/// <summary>
/// Writes solid quads, lines and rings as mesh triangles for the input visualizers.
/// </summary>
public static class FeedbackShapes
{
    /// <summary>
    /// Gets the number of vertices that <see cref="WriteQuad"/> and <see cref="WriteLine"/> write.
    /// </summary>
    public static int QuadVertexCount => 6;

    /// <summary>
    /// Returns the number of vertices that <see cref="WriteRing"/> writes.
    /// </summary>
    /// <param name="segments">The segment count. A count below 3 counts as 3.</param>
    /// <returns>Six vertices for each segment.</returns>
    public static int RingVertexCount(int segments) => Math.Max(3, segments) * 6;

    /// <summary>
    /// Writes a quad as two triangles with no texture coordinates. The corners go around the quad in order.
    /// </summary>
    /// <param name="into">The destination, at least six vertices long.</param>
    /// <param name="x0">The X coordinate of the first corner.</param>
    /// <param name="y0">The Y coordinate of the first corner.</param>
    /// <param name="x1">The X coordinate of the second corner.</param>
    /// <param name="y1">The Y coordinate of the second corner.</param>
    /// <param name="x2">The X coordinate of the third corner.</param>
    /// <param name="y2">The Y coordinate of the third corner.</param>
    /// <param name="x3">The X coordinate of the fourth corner.</param>
    /// <param name="y3">The Y coordinate of the fourth corner.</param>
    /// <param name="color">The vertex color, premultiplied.</param>
    public static void WriteQuad(
        Span<MeshVertex> into,
        double x0,
        double y0,
        double x1,
        double y1,
        double x2,
        double y2,
        double x3,
        double y3,
        in RenderColor color)
    {
        into[0] = new MeshVertex((float)x0, (float)y0, 0f, 0f, color);
        into[1] = new MeshVertex((float)x1, (float)y1, 0f, 0f, color);
        into[2] = new MeshVertex((float)x3, (float)y3, 0f, 0f, color);
        into[3] = new MeshVertex((float)x1, (float)y1, 0f, 0f, color);
        into[4] = new MeshVertex((float)x2, (float)y2, 0f, 0f, color);
        into[5] = new MeshVertex((float)x3, (float)y3, 0f, 0f, color);
    }

    /// <summary>
    /// Writes a line segment as a quad. A segment shorter than a millionth of a pixel draws horizontally.
    /// </summary>
    /// <param name="into">The destination, at least six vertices long.</param>
    /// <param name="x0">The X coordinate of the start.</param>
    /// <param name="y0">The Y coordinate of the start.</param>
    /// <param name="x1">The X coordinate of the end.</param>
    /// <param name="y1">The Y coordinate of the end.</param>
    /// <param name="width">The line width. The method draws at least one pixel.</param>
    /// <param name="color">The vertex color, premultiplied.</param>
    public static void WriteLine(
        Span<MeshVertex> into, double x0, double y0, double x1, double y1, double width, in RenderColor color)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-6)
        {
            dx = 1;
            dy = 0;
            length = 1;
        }

        var half = Math.Max(0.5, width / 2.0);
        var nx = -dy / length * half;
        var ny = dx / length * half;
        WriteQuad(into, x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny, color);
    }

    /// <summary>
    /// Writes a ring as one quad for each segment.
    /// </summary>
    /// <param name="into">The destination, at least <see cref="RingVertexCount"/> vertices long.</param>
    /// <param name="centerX">The X coordinate of the center.</param>
    /// <param name="centerY">The Y coordinate of the center.</param>
    /// <param name="radius">The radius of the middle of the ring.</param>
    /// <param name="width">The ring width. The method draws at least one pixel.</param>
    /// <param name="color">The vertex color, premultiplied.</param>
    /// <param name="segments">The segment count. A count below 3 counts as 3.</param>
    public static void WriteRing(
        Span<MeshVertex> into,
        double centerX,
        double centerY,
        double radius,
        double width,
        in RenderColor color,
        int segments = 48)
    {
        var count = Math.Max(3, segments);
        var half = Math.Max(0.5, width / 2.0);
        var inner = Math.Max(0, radius - half);
        var outer = radius + half;
        for (var i = 0; i < count; i++)
        {
            var a0 = i / (double)count * Math.PI * 2;
            var a1 = (i + 1) / (double)count * Math.PI * 2;
            var cos0 = Math.Cos(a0);
            var sin0 = Math.Sin(a0);
            var cos1 = Math.Cos(a1);
            var sin1 = Math.Sin(a1);
            WriteQuad(
                into.Slice(i * 6, 6),
                centerX + (cos0 * inner), centerY + (sin0 * inner),
                centerX + (cos1 * inner), centerY + (sin1 * inner),
                centerX + (cos1 * outer), centerY + (sin1 * outer),
                centerX + (cos0 * outer), centerY + (sin0 * outer),
                color);
        }
    }

    /// <summary>
    /// Scales every channel of a color by an alpha.
    /// </summary>
    /// <param name="color">The color to scale.</param>
    /// <param name="alpha">The factor. The method clamps it to the range from 0 to 1.</param>
    /// <returns>The scaled color.</returns>
    public static RenderColor Premultiplied(in RenderColor color, double alpha)
    {
        var a = (float)Math.Clamp(alpha, 0, 1);
        return new RenderColor(color.R * a, color.G * a, color.B * a, color.A * a);
    }
}
