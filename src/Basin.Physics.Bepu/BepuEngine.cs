namespace Basin.Physics;

/// <summary>
/// Runs a <see cref="PhysicsWorld"/> on bepuphysics2, with every body held to the plane z = 0. Each world has its own simulation, so there is no limit on the number of worlds.
/// </summary>
public sealed class BepuEngine : PhysicsEngine
{
    private BepuEngine()
    {
    }

    /// <summary>
    /// Gets the one instance of the engine.
    /// </summary>
    public static BepuEngine Instance { get; } = new();

    /// <inheritdoc/>
    public override string Name => "bepu";

    /// <summary>
    /// Calculates the decay rate of a body with the given damping. The engine applies the damping as an exact exponential, so the rate is the damping itself.
    /// </summary>
    /// <param name="linearDamping">The <see cref="PhysicsMaterial.LinearDamping"/> of the body.</param>
    /// <returns><paramref name="linearDamping"/>, per second.</returns>
    public override double DecayRate(double linearDamping) => linearDamping;

    internal override IPhysicsBackend CreateBackend(double gravityX, double gravityY, double impactThreshold) =>
        new BepuBackend(gravityX, gravityY, impactThreshold);
}
