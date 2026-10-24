namespace Basin.Physics;

/// <summary>
/// Holds the surface and mass values of a body. Both engines read the same values, but the same restitution bounces less on bepu than on Box2D.
/// </summary>
/// <param name="Density">The mass per unit area in a planar world, or per unit volume in a 3D world.</param>
/// <param name="Friction">The friction coefficient. A pair uses the geometric mean of its two values.</param>
/// <param name="Restitution">The bounce, from 0 to 1. A pair uses the larger of its two values.</param>
/// <param name="GravityScale">The multiplier on the world gravity for this body. Zero makes the body ignore gravity.</param>
/// <param name="LinearDamping">The linear damping rate, per second. <see cref="PhysicsEngine.DecayRate"/> converts it into the decay rate that the engine applies.</param>
public readonly record struct PhysicsMaterial(
    double Density = 1.0,
    double Friction = 0.3,
    double Restitution = 0.2,
    double GravityScale = 1.0,
    double LinearDamping = 1.0)
{
    /// <summary>
    /// Gets the material with the default value of each parameter.
    /// </summary>
    public static PhysicsMaterial Default => new(1.0, 0.3, 0.2, 1.0, 1.0);
}
