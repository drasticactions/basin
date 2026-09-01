using SkiaSharp;

namespace DeskbarWm;

internal sealed class HvifPath
{
    public List<(SKPoint Point, SKPoint In, SKPoint Out)> Points { get; } = [];

    public bool Closed { get; set; }

    public SKPath ToPath()
    {
        var builder = new SKPathBuilder();
        if (Points.Count > 0)
        {
            builder.MoveTo(Points[0].Point);
            for (var i = 1; i < Points.Count; i++)
            {
                AddSegment(builder, Points[i - 1], Points[i]);
            }

            if (Closed && Points.Count > 1)
            {
                AddSegment(builder, Points[^1], Points[0]);
                builder.Close();
            }
        }

        return builder.Detach();
    }

    private static void AddSegment(
        SKPathBuilder builder,
        (SKPoint Point, SKPoint In, SKPoint Out) from,
        (SKPoint Point, SKPoint In, SKPoint Out) to)
    {
        if (from.Out == from.Point && to.In == to.Point)
        {
            builder.LineTo(to.Point);
        }
        else
        {
            builder.CubicTo(from.Out, to.In, to.Point);
        }
    }
}
