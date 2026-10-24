namespace Basin.Physics;

/// <summary>
/// Names how the solver moves a body.
/// </summary>
public enum PhysicsBodyKind
{
    /// <summary>The body does not move and other bodies cannot push it. It never keeps the world awake.</summary>
    Static,
    /// <summary>The body moves only by its velocity or a drag target. It pushes dynamic bodies, but contacts do not push it.</summary>
    Kinematic,
    /// <summary>The body moves under gravity, contacts and its velocity, and it can sleep.</summary>
    Dynamic,
}
