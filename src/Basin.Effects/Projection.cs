namespace Basin.Effects;

/// <summary>
/// Builds projective transforms that map a rectangle onto a quadrilateral. A renderer draws the result through its ordinary transform, with no 3D pass.
/// </summary>
public static class Projection
{
    /// <summary>
    /// Calculates the homography that maps the corners of a rectangle onto four points.
    /// </summary>
    /// <param name="rect">The source rectangle.</param>
    /// <param name="topLeft">The target of the top-left corner.</param>
    /// <param name="topRight">The target of the top-right corner.</param>
    /// <param name="bottomLeft">The target of the bottom-left corner.</param>
    /// <param name="bottomRight">The target of the bottom-right corner.</param>
    /// <returns>The transform. When the target points are degenerate, it collapses the rectangle onto <paramref name="topLeft"/>.</returns>
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

    /// <summary>
    /// Calculates the transform that scales a rectangle about its center, turns it about a vertical axis and places it at a point, with a pinhole camera.
    /// </summary>
    /// <param name="bounds">The source rectangle.</param>
    /// <param name="centerX">The horizontal position of the result center.</param>
    /// <param name="centerY">The vertical position of the result center.</param>
    /// <param name="scale">The scale before the turn.</param>
    /// <param name="yawRadians">The turn about the vertical axis, in radians.</param>
    /// <param name="cameraDistance">The distance from the camera to the plane, in pixels.</param>
    /// <returns>The projective transform.</returns>
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

    /// <summary>
    /// Projects a rectangle-local point at a depth through KWin's 60-degree frustum. At depth zero the result is the point itself, offset by the rectangle origin.
    /// </summary>
    /// <param name="rect">The rectangle that is the viewport.</param>
    /// <param name="localX">The horizontal position relative to the rectangle.</param>
    /// <param name="localY">The vertical position relative to the rectangle.</param>
    /// <param name="z">The depth. Negative values move away from the viewer.</param>
    /// <returns>The projected point in the coordinates of <paramref name="rect"/>.</returns>
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

    /// <summary>
    /// Calculates the transform that turns a rectangle about one of its edges and moves it back, through <see cref="FrustumPoint"/>.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="edge">The edge that stays in place, before the depth.</param>
    /// <param name="angleDegrees">The turn, in degrees.</param>
    /// <param name="distance">The distance that the rectangle moves away from the viewer.</param>
    /// <returns>The projective transform.</returns>
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
