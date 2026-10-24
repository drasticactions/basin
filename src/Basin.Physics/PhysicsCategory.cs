namespace Basin.Physics;

/// <summary>
/// Names the collision groups of a planar world. A body mask that combines groups collides with each group that it names.
/// </summary>
[Flags]
public enum PhysicsCategory : ulong
{
    /// <summary>No group. A body with this mask collides with nothing.</summary>
    None = 0,
    /// <summary>The bodies that <see cref="PhysicsWorld.CreateBody"/> makes.</summary>
    Windows = 1,
    /// <summary>The outer walls that <see cref="PhysicsWalls"/> puts around the bounding box.</summary>
    Edges = 2,
    /// <summary>The fences that <see cref="PhysicsWalls"/> puts over the dead zones inside the bounding box.</summary>
    Fences = 4,
    /// <summary>Every group. A new body has this mask.</summary>
    All = Windows | Edges | Fences,
}
