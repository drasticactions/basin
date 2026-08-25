namespace Basin.Effects;

public static class FeedbackShapes
{
    public static int QuadVertexCount => 6;

    public static int RingVertexCount(int segments) => Math.Max(3, segments) * 6;

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

    public static RenderColor Premultiplied(in RenderColor color, double alpha)
    {
        var a = (float)Math.Clamp(alpha, 0, 1);
        return new RenderColor(color.R * a, color.G * a, color.B * a, color.A * a);
    }
}
