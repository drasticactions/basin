namespace Basin.Scene;

internal readonly record struct ClipVertex(double U, double V, double X, double Y)
{
    public static int Clip(Span<ClipVertex> polygon, Span<ClipVertex> spare, int count, double minU, double minV, double maxU, double maxV)
    {
        count = Edge(polygon, spare, count, 0, minU, keepAbove: true);
        count = Edge(spare, polygon, count, 0, maxU, keepAbove: false);
        count = Edge(polygon, spare, count, 1, minV, keepAbove: true);
        count = Edge(spare, polygon, count, 1, maxV, keepAbove: false);
        return count;
    }

    private static int Edge(ReadOnlySpan<ClipVertex> input, Span<ClipVertex> output, int count, int axis, double limit, bool keepAbove)
    {
        var written = 0;
        for (var i = 0; i < count; i++)
        {
            var current = input[i];
            var next = input[(i + 1) % count];
            var currentIn = Inside(current, axis, limit, keepAbove);
            var nextIn = Inside(next, axis, limit, keepAbove);
            if (currentIn)
            {
                output[written++] = current;
            }

            if (currentIn != nextIn)
            {
                var a = axis == 0 ? current.U : current.V;
                var b = axis == 0 ? next.U : next.V;
                var t = (limit - a) / (b - a);
                output[written++] = new ClipVertex(
                    current.U + ((next.U - current.U) * t),
                    current.V + ((next.V - current.V) * t),
                    current.X + ((next.X - current.X) * t),
                    current.Y + ((next.Y - current.Y) * t));
            }
        }

        return written;
    }

    private static bool Inside(in ClipVertex vertex, int axis, double limit, bool keepAbove)
    {
        var value = axis == 0 ? vertex.U : vertex.V;
        return keepAbove ? value >= limit : value <= limit;
    }
}
