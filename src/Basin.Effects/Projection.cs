namespace Basin.Effects;

public static class Projection
{
    public static RenderTransform MapRect(
        in Box rect,
        (double X, double Y) topLeft,
        (double X, double Y) topRight,
        (double X, double Y) bottomLeft,
        (double X, double Y) bottomRight)
    {
        var sx = topLeft.X - topRight.X + bottomRight.X - bottomLeft.X;
        var sy = topLeft.Y - topRight.Y + bottomRight.Y - bottomLeft.Y;
        double g = 0, h = 0;
        if (Math.Abs(sx) > 1e-7 || Math.Abs(sy) > 1e-7)
        {
            var dx1 = topRight.X - bottomRight.X;
            var dx2 = bottomLeft.X - bottomRight.X;
            var dy1 = topRight.Y - bottomRight.Y;
            var dy2 = bottomLeft.Y - bottomRight.Y;
            var det = (dx1 * dy2) - (dx2 * dy1);
            if (Math.Abs(det) < 1e-12)
            {
                return new RenderTransform(0, 0, topLeft.X, 0, 0, topLeft.Y, 0, 0, 1);
            }

            g = ((sx * dy2) - (dx2 * sy)) / det;
            h = ((dx1 * sy) - (sx * dy1)) / det;
        }

        var unit = new RenderTransform(
            topRight.X - topLeft.X + (g * topRight.X), bottomLeft.X - topLeft.X + (h * bottomLeft.X), topLeft.X,
            topRight.Y - topLeft.Y + (g * topRight.Y), bottomLeft.Y - topLeft.Y + (h * bottomLeft.Y), topLeft.Y,
            g, h, 1);
        var normalize = RenderTransform.Multiply(
            RenderTransform.Scale(1.0 / rect.Width, 1.0 / rect.Height),
            RenderTransform.Translation(-rect.X, -rect.Y));
        return RenderTransform.Multiply(unit, normalize);
    }

    public static RenderTransform Card(
        in Box bounds, double centerX, double centerY, double scale, double yawRadians, double cameraDistance)
    {
        var cx = bounds.X + (bounds.Width / 2.0);
        var cy = bounds.Y + (bounds.Height / 2.0);
        var sin = Math.Sin(yawRadians);
        var cos = Math.Cos(yawRadians);

        (double X, double Y) Corner(double x, double y)
        {
            var u = (x - cx) * scale;
            var v = (y - cy) * scale;
            var w = cameraDistance - (u * sin);
            if (w < 1e-6)
            {
                w = 1e-6;
            }

            var f = cameraDistance / w;
            return (centerX + (u * cos * f), centerY + (v * f));
        }

        return MapRect(
            bounds,
            Corner(bounds.X, bounds.Y),
            Corner(bounds.Right, bounds.Y),
            Corner(bounds.X, bounds.Bottom),
            Corner(bounds.Right, bounds.Bottom));
    }

    public static (double X, double Y) FrustumPoint(in Box rect, double localX, double localY, double z)
    {
        var depth = 1.1 - (0.001 * z);
        if (Math.Abs(depth) < 1e-9)
        {
            depth = depth < 0 ? -1e-9 : 1e-9;
        }

        var width = rect.Width == 0 ? 1 : rect.Width;
        var height = rect.Height == 0 ? 1 : rect.Height;
        var ndcX = 1.1 * ((2.0 * localX / width) - 1.0) / depth;
        var ndcY = 1.1 * ((2.0 * localY / height) - 1.0) / depth;
        return (rect.X + (width / 2.0) + (width / 2.0 * ndcX), rect.Y + (height / 2.0) + (height / 2.0 * ndcY));
    }

    public static RenderTransform Frustum(in Box rect, FrustumEdge edge, double angleDegrees, double distance)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var sin = Math.Sin(radians);
        var cos = Math.Cos(radians);
        var width = (double)rect.Width;
        var height = (double)rect.Height;
        var box = rect;

        (double X, double Y) Corner(double x, double y)
        {
            double qx = x, qy = y, qz;
            switch (edge)
            {
                case FrustumEdge.Right:
                    qx = ((x - width) * cos) + width;
                    qz = ((x - width) * sin) - distance;
                    break;
                case FrustumEdge.Bottom:
                    qy = ((y - height) * cos) + height;
                    qz = ((y - height) * sin) - distance;
                    break;
                case FrustumEdge.Left:
                    qx = x * cos;
                    qz = (-x * sin) - distance;
                    break;
                default:
                    qy = y * cos;
                    qz = (-y * sin) - distance;
                    break;
            }

            return FrustumPoint(box, qx, qy, qz);
        }

        return MapRect(
            rect,
            Corner(0, 0),
            Corner(width, 0),
            Corner(0, height),
            Corner(width, height));
    }
}
