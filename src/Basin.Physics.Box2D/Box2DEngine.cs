namespace Basin.Physics;

/// <summary>
/// Runs a <see cref="PhysicsWorld"/> on Box2D.NET. The package keeps a process-wide table of 128 worlds, and the engine starts no thread.
/// </summary>
public sealed class Box2DEngine : PhysicsEngine
{
    private Box2DEngine()
    {
    }

    /// <summary>
    /// Gets the one instance of the engine.
    /// </summary>
    public static Box2DEngine Instance { get; } = new();

    /// <inheritdoc/>
    public override string Name => "box2d";

    /// <summary>
    /// Calculates the decay rate of a body with the given damping. Box2D divides the velocity by 1 + h c on each substep of length h, so the rate is ln(1 + h c) / h.
    /// </summary>
    /// <param name="linearDamping">The <see cref="PhysicsMaterial.LinearDamping"/> of the body.</param>
    /// <returns>The decay rate per second.</returns>
    public override double DecayRate(double linearDamping)
    {
        var sub = PhysicsWorld.StepNanos / 1e9 / PhysicsWorld.SubSteps;
        return Math.Log(1 + (sub * linearDamping)) / sub;
    }

    internal override IPhysicsBackend CreateBackend(double gravityX, double gravityY, double impactThreshold) =>
        new Box2DBackend(gravityX, gravityY, impactThreshold);
}
