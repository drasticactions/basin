using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A ray in world space or in a model space. A distance along it is in units of the direction's length.
/// </summary>
/// <param name="Origin">The start point of the ray.</param>
/// <param name="Direction">The direction. <see cref="WorldView.RayAt"/> gives a unit vector, so its distances are in meters.</param>
public readonly record struct WorldRay(Vector3 Origin, Vector3 Direction)
{
    /// <summary>
    /// Gets the point at a distance along the ray.
    /// </summary>
    /// <param name="distance">The ray parameter, in units of the direction's length.</param>
    /// <returns>The origin plus the direction times <paramref name="distance"/>.</returns>
    public Vector3 At(float distance) => Origin + (Direction * distance);

    /// <summary>
    /// Moves the ray into another space. The direction is not normalized again, so distances stay comparable with the original ray.
    /// </summary>
    /// <param name="matrix">The transform, for example the inverse of a model matrix.</param>
    /// <returns>The ray in the new space.</returns>
    public WorldRay Transformed(in Matrix4x4 matrix) =>
        new(Vector3.Transform(Origin, matrix), Vector3.TransformNormal(Direction, matrix));

    /// <summary>
    /// Intersects the ray with a plane from either side.
    /// </summary>
    /// <param name="point">A point on the plane.</param>
    /// <param name="normal">The plane normal. It need not be a unit vector.</param>
    /// <param name="distance">The ray parameter of the intersection, or 0 when the ray is parallel to the plane.</param>
    /// <returns><see langword="true"/> when the plane is at or ahead of the origin. <see langword="false"/> when the ray is parallel or the plane is behind.</returns>
    public bool TryPlane(Vector3 point, Vector3 normal, out float distance)
    {
        var facing = Vector3.Dot(Direction, normal);
        if (MathF.Abs(facing) < 1e-7f)
        {
            distance = 0;
            return false;
        }

        distance = Vector3.Dot(point - Origin, normal) / facing;
        return distance >= 0;
    }

    /// <summary>
    /// Intersects the ray with an axis-aligned box by the slab method.
    /// </summary>
    /// <param name="box">The box, in the ray's space.</param>
    /// <param name="distance">The ray parameter of the entry point. It is 0 when the origin is inside the box or when the ray misses.</param>
    /// <returns><see langword="true"/> when the ray hits the box at or ahead of its origin.</returns>
    public bool TryBox(in WorldBounds box, out float distance)
    {
        var near = 0f;
        var far = float.MaxValue;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = axis == 0 ? Origin.X : axis == 1 ? Origin.Y : Origin.Z;
            var direction = axis == 0 ? Direction.X : axis == 1 ? Direction.Y : Direction.Z;
            var min = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
            var max = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
            if (MathF.Abs(direction) < 1e-9f)
            {
                if (origin < min || origin > max)
                {
                    distance = 0;
                    return false;
                }

                continue;
            }

            var t1 = (min - origin) / direction;
            var t2 = (max - origin) / direction;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            near = MathF.Max(near, t1);
            far = MathF.Min(far, t2);
            if (near > far)
            {
                distance = 0;
                return false;
            }
        }

        distance = near;
        return true;
    }
}
