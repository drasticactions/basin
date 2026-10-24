namespace Basin.Physics;

/// <summary>
/// Names the degrees of freedom that the solver holds fixed for a body. A combination locks each named axis.
/// </summary>
[Flags]
public enum PhysicsLocks
{
    /// <summary>No axis is locked.</summary>
    None = 0,
    /// <summary>The body does not move along the x axis.</summary>
    X = 1,
    /// <summary>The body does not move along the y axis.</summary>
    Y = 2,
    /// <summary>The body keeps its current angle and gets no spin from contacts or throws.</summary>
    Rotation = 4,
}
