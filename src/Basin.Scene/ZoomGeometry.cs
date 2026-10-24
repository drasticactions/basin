namespace Basin.Scene;

internal static class ZoomGeometry
{
    public const double MaxZoom = 16.0;

    public static double ClampFactor(double factor) => double.IsFinite(factor) ? Math.Clamp(factor, 1.0, MaxZoom) : 1.0;

    public static (double Width, double Height) LogicalSize(IOutput output)
    {
        var plain = OutputProjection.For(output);
        return (plain.Width / plain.Scale, plain.Height / plain.Scale);
    }

    public static (double X, double Y) ClampCenter(IOutput output, Point position, double factor, double centerX, double centerY)
    {
        if (factor <= 1.0)
        {
            return (centerX, centerY);
        }

        var (width, height) = LogicalSize(output);
        var halfWidth = width / factor / 2;
        var halfHeight = height / factor / 2;
        return (
            Math.Clamp(centerX, position.X + halfWidth, position.X + width - halfWidth),
            Math.Clamp(centerY, position.Y + halfHeight, position.Y + height - halfHeight));
    }

    public static (double Left, double Top) Origin(double width, double height, Point position, double factor, double centerX, double centerY)
    {
        var visibleWidth = width / factor;
        var visibleHeight = height / factor;
        var left = Math.Clamp(centerX - position.X - (visibleWidth / 2), 0, width - visibleWidth);
        var top = Math.Clamp(centerY - position.Y - (visibleHeight / 2), 0, height - visibleHeight);
        return (left, top);
    }

    public static (double X, double Y, double Width, double Height) Area(IOutput output, Point position, double factor, double centerX, double centerY)
    {
        var (width, height) = LogicalSize(output);
        if (factor <= 1.0)
        {
            return (position.X, position.Y, width, height);
        }

        var (left, top) = Origin(width, height, position, factor, centerX, centerY);
        return (position.X + left, position.Y + top, width / factor, height / factor);
    }

    public static OutputProjection Project(IOutput output, Point position, double factor, double centerX, double centerY)
    {
        var plain = OutputProjection.For(output);
        if (factor <= 1.0)
        {
            return plain;
        }

        var zoomedScale = plain.Scale * factor;
        var zoomedWidth = (int)Math.Round(plain.Width * factor);
        var zoomedHeight = (int)Math.Round(plain.Height * factor);
        var (left, top) = Origin(plain.Width / plain.Scale, plain.Height / plain.Scale, position, factor, centerX, centerY);
        var visible = new Box((int)Math.Round(left * zoomedScale), (int)Math.Round(top * zoomedScale), plain.Width, plain.Height);
        var origin = plain.Transform.Invert().Apply(visible, zoomedWidth, zoomedHeight);
        return new OutputProjection(zoomedScale, plain.Transform, zoomedWidth, zoomedHeight, origin.X, origin.Y);
    }
}
