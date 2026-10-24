namespace Basin.Physics;

/// <summary>
/// Names the engine that a <see cref="PhysicsWorld"/> runs on. Only the engine packages can derive from it.
/// </summary>
public abstract class PhysicsEngine
{
    private protected PhysicsEngine()
    {
    }

    /// <summary>
    /// Gets the short lowercase name of the engine, for logs and configuration.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Calculates the exponential decay rate of the speed of a body with the given damping on this engine. <see cref="Kinematics.Fling.ThrowTo"/> uses it as the friction to aim a throw.
    /// </summary>
    /// <param name="linearDamping">The <see cref="PhysicsMaterial.LinearDamping"/> of the body.</param>
    /// <returns>The decay rate per second.</returns>
    public abstract double DecayRate(double linearDamping);

    internal abstract IPhysicsBackend CreateBackend(double gravityX, double gravityY, double impactThreshold);
}
