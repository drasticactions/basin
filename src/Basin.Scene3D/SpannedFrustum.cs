using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// The math of one camera spanned over every output. The union of the layout is one image plane, and each output takes an off-axis slice of it.
/// </summary>
public static class SpannedFrustum
{
    /// <summary>
    /// Maps the four edges of an output's slice onto the near plane.
    /// </summary>
    /// <param name="union">The union of the output layout, in layout pixels.</param>
    /// <param name="slice">The output's box in the layout, in layout pixels.</param>
    /// <param name="fieldOfView">The vertical field of view of the union, in radians.</param>
    /// <param name="near">The distance to the near plane, in meters.</param>
    /// <returns>The edges of the slice on the near plane, in camera space.</returns>
    /// <exception cref="ArgumentException"><paramref name="union"/> has no area.</exception>
    public static (float Left, float Right, float Bottom, float Top) Edges(
        in Box union, in Box slice, float fieldOfView, float near)
    {
        if (union.Width <= 0 || union.Height <= 0)
        {
            throw new ArgumentException("The layout union is empty.", nameof(union));
        }

        var halfHeight = near * MathF.Tan(fieldOfView * 0.5f);
        var halfWidth = halfHeight * union.Width / union.Height;
        var left = PlaneX(slice.X, union, halfWidth);
        var right = PlaneX(slice.X + slice.Width, union, halfWidth);
        var top = PlaneY(slice.Y, union, halfHeight);
        var bottom = PlaneY(slice.Y + slice.Height, union, halfHeight);
        return (left, right, bottom, top);
    }

    /// <summary>
    /// Builds the off-axis projection of an output's slice. Clip depth is in [0, 1] and clip y is up.
    /// </summary>
    /// <param name="union">The union of the output layout, in layout pixels.</param>
    /// <param name="slice">The output's box in the layout.</param>
    /// <param name="fieldOfView">The vertical field of view of the union, in radians.</param>
    /// <param name="near">The distance to the near plane, in meters.</param>
    /// <param name="far">The distance to the far plane, in meters.</param>
    /// <returns>A perspective projection from <see cref="Matrix4x4.CreatePerspectiveOffCenter"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="union"/> has no area.</exception>
    public static Matrix4x4 Projection(in Box union, in Box slice, float fieldOfView, float near, float far)
    {
        var (left, right, bottom, top) = Edges(union, slice, fieldOfView, near);
        return Matrix4x4.CreatePerspectiveOffCenter(left, right, bottom, top, near, far);
    }

    /// <summary>
    /// Gives the direction through a layout pixel in camera space. The result does not depend on the output under the pixel.
    /// </summary>
    /// <param name="union">The union of the output layout, in layout pixels.</param>
    /// <param name="layoutX">The x coordinate in layout pixels.</param>
    /// <param name="layoutY">The y coordinate in layout pixels.</param>
    /// <param name="fieldOfView">The vertical field of view of the union, in radians.</param>
    /// <returns>A unit vector. The camera looks down -z.</returns>
    public static Vector3 CameraDirection(in Box union, double layoutX, double layoutY, float fieldOfView)
    {
        var halfHeight = MathF.Tan(fieldOfView * 0.5f);
        var halfWidth = halfHeight * union.Width / union.Height;
        var x = (float)(((layoutX - union.X) / union.Width * 2) - 1) * halfWidth;
        var y = (float)(1 - ((layoutY - union.Y) / union.Height * 2)) * halfHeight;
        return Vector3.Normalize(new Vector3(x, y, -1));
    }

    private static float PlaneX(double x, in Box union, float halfWidth) =>
        (float)(((x - union.X) / union.Width * 2) - 1) * halfWidth;

    private static float PlaneY(double y, in Box union, float halfHeight) =>
        (float)(1 - ((y - union.Y) / union.Height * 2)) * halfHeight;
}
