using Basin;
using Basin.Scene;

namespace EightWm;

internal sealed class TileTilt : IMeshTransform
{
    private const int Resolution = 6;
    private const double MaxRecess = 0.075;

    private static readonly RenderColor White = new(1f, 1f, 1f, 1f);

    public double Press { get; set; }

    public double ContactU { get; set; }

    public double ContactV { get; set; }

    public void SetContact(double x, double y, in Box bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            ContactU = 0;
            ContactV = 0;
            return;
        }

        ContactU = Math.Clamp(((x - bounds.X) / bounds.Width * 2) - 1, -1, 1);
        ContactV = Math.Clamp(((y - bounds.Y) / bounds.Height * 2) - 1, -1, 1);
    }

    public Box MapBounds(in Box childBounds) => childBounds;

    public int VertexCount(in Box childBounds) => Resolution * Resolution * 6;

    public void WriteVertices(in Box childBounds, Span<MeshVertex> into)
    {
        var write = 0;
        for (var row = 0; row < Resolution; row++)
        {
            for (var column = 0; column < Resolution; column++)
            {
                var u0 = (double)column / Resolution;
                var u1 = (double)(column + 1) / Resolution;
                var v0 = (double)row / Resolution;
                var v1 = (double)(row + 1) / Resolution;

                var left = childBounds.X + (childBounds.Width * u0);
                var right = childBounds.X + (childBounds.Width * u1);
                var top = childBounds.Y + (childBounds.Height * v0);
                var bottom = childBounds.Y + (childBounds.Height * v1);

                var a = Point(childBounds, u0, v0);
                var b = Point(childBounds, u1, v0);
                var c = Point(childBounds, u0, v1);
                var d = Point(childBounds, u1, v1);

                into[write++] = Vertex(a, left, top);
                into[write++] = Vertex(b, right, top);
                into[write++] = Vertex(c, left, bottom);
                into[write++] = Vertex(b, right, top);
                into[write++] = Vertex(d, right, bottom);
                into[write++] = Vertex(c, left, bottom);
            }
        }
    }

    private (double X, double Y) Point(in Box bounds, double u, double v)
    {
        var x = bounds.X + (bounds.Width * u);
        var y = bounds.Y + (bounds.Height * v);
        if (Press <= 0)
        {
            return (x, y);
        }

        var centerX = bounds.X + (bounds.Width / 2.0);
        var centerY = bounds.Y + (bounds.Height / 2.0);
        var localU = (u * 2) - 1;
        var localV = (v * 2) - 1;
        var reach = Math.Sqrt(
            ((localU - ContactU) * (localU - ContactU)) + ((localV - ContactV) * (localV - ContactV)));
        var nearness = Math.Clamp(1 - (reach / 2.0), 0, 1);
        var recess = 1 - (Press * MaxRecess * (0.45 + (0.55 * nearness)));
        return (centerX + ((x - centerX) * recess), centerY + ((y - centerY) * recess));
    }

    private static MeshVertex Vertex((double X, double Y) point, double sourceX, double sourceY) =>
        new((float)point.X, (float)point.Y, (float)sourceX, (float)sourceY, White);
}
