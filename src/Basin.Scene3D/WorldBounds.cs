using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// An axis-aligned box. The box is empty when a minimum is greater than its maximum on any axis.
/// </summary>
/// <param name="Min">The corner with the smallest coordinates.</param>
/// <param name="Max">The corner with the largest coordinates.</param>
public readonly record struct WorldBounds(Vector3 Min, Vector3 Max)
{
    /// <summary>
    /// Gets an empty box. <see cref="Include"/> and <see cref="Union"/> on it give the other operand.
    /// </summary>
    public static WorldBounds Empty => new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    /// <summary>
    /// Gets a value indicating whether the box holds no point.
    /// </summary>
    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    /// <summary>
    /// Gets the extent on each axis. It is zero for an empty box.
    /// </summary>
    public Vector3 Size => IsEmpty ? Vector3.Zero : Max - Min;

    /// <summary>
    /// Gets the midpoint of the two corners. It has no meaning for an empty box.
    /// </summary>
    public Vector3 Center => (Min + Max) * 0.5f;

    /// <summary>
    /// Grows the box to hold a point.
    /// </summary>
    /// <param name="point">The point to hold.</param>
    /// <returns>The smallest box that holds this box and <paramref name="point"/>.</returns>
    public WorldBounds Include(Vector3 point) => new(Vector3.Min(Min, point), Vector3.Max(Max, point));

    /// <summary>
    /// Grows the box to hold another box. An empty operand does not change the result.
    /// </summary>
    /// <param name="other">The box to hold.</param>
    /// <returns>The smallest box that holds both boxes.</returns>
    public WorldBounds Union(in WorldBounds other) =>
        other.IsEmpty ? this : IsEmpty ? other : new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    /// <summary>
    /// Tests a point against the box. The faces are inside.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> when the point is inside the box or on a face.</returns>
    public bool Contains(Vector3 point) =>
        point.X >= Min.X && point.X <= Max.X &&
        point.Y >= Min.Y && point.Y <= Max.Y &&
        point.Z >= Min.Z && point.Z <= Max.Z;

    /// <summary>
    /// Tests two boxes for overlap. Boxes that only touch overlap.
    /// </summary>
    /// <param name="other">The other box.</param>
    /// <returns><see langword="true"/> when the boxes share a point.</returns>
    public bool Intersects(in WorldBounds other) =>
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
        Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

    /// <summary>
    /// Transforms the eight corners and gives the axis-aligned box round them. The result can be larger than the transformed box.
    /// </summary>
    /// <param name="matrix">The transform, for example a model matrix.</param>
    /// <returns>The box round the transformed corners, or this box when it is empty.</returns>
    public WorldBounds Transformed(in Matrix4x4 matrix)
    {
        if (IsEmpty)
        {
            return this;
        }

        var result = Empty;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? Min.X : Max.X,
                (i & 2) == 0 ? Min.Y : Max.Y,
                (i & 4) == 0 ? Min.Z : Max.Z);
            result = result.Include(Vector3.Transform(corner, matrix));
        }

        return result;
    }
}
