namespace Basin.Physics;

/// <summary>
/// Names the shape of a dynamic body in a <see cref="PhysicsWorld3D"/>.
/// </summary>
public enum PhysicsShape3D
{
    /// <summary>A box, given by its half extents on each axis.</summary>
    Box,
    /// <summary>An upright cylinder along the y axis, given by its radius and half height.</summary>
    Cylinder,
}
