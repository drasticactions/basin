using System.Numerics;

namespace Basin.Physics;

/// <summary>
/// Holds the position and orientation of a body in a <see cref="PhysicsWorld3D"/>, in meters.
/// </summary>
/// <param name="Position">The position of the body center in world space.</param>
/// <param name="Orientation">The rotation from the body frame to world space.</param>
public readonly record struct PhysicsPose3D(Vector3 Position, Quaternion Orientation)
{
    /// <summary>
    /// Makes a pose at a position with no rotation.
    /// </summary>
    /// <param name="position">The position in world space.</param>
    /// <returns>A pose with the identity orientation.</returns>
    public static PhysicsPose3D At(Vector3 position) => new(position, Quaternion.Identity);

    /// <summary>
    /// Gets the matrix that rotates by <see cref="Orientation"/> and then translates by <see cref="Position"/>.
    /// </summary>
    public Matrix4x4 Matrix => Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Position);

    /// <summary>
    /// Converts a point from the body frame to world space.
    /// </summary>
    /// <param name="local">The point in the body frame.</param>
    /// <returns>The point in world space.</returns>
    public Vector3 ToWorld(Vector3 local) => Position + Vector3.Transform(local, Orientation);

    /// <summary>
    /// Converts a point from world space to the body frame. It assumes that <see cref="Orientation"/> is a unit quaternion.
    /// </summary>
    /// <param name="world">The point in world space.</param>
    /// <returns>The point in the body frame.</returns>
    public Vector3 ToLocal(Vector3 world) => Vector3.Transform(world - Position, Quaternion.Conjugate(Orientation));

    /// <summary>
    /// Blends two poses. The position is linear and the orientation is spherical.
    /// </summary>
    /// <param name="from">The pose at an amount of 0.</param>
    /// <param name="to">The pose at an amount of 1.</param>
    /// <param name="amount">The blend factor, from 0 to 1.</param>
    /// <returns>The blended pose.</returns>
    public static PhysicsPose3D Lerp(in PhysicsPose3D from, in PhysicsPose3D to, float amount) =>
        new(Vector3.Lerp(from.Position, to.Position, amount), Quaternion.Slerp(from.Orientation, to.Orientation, amount));
}
