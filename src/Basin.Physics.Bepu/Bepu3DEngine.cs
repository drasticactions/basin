using System.Numerics;

namespace Basin.Physics;

/// <summary>
/// Runs a <see cref="PhysicsWorld3D"/> on bepuphysics2. Each world has its own simulation and buffer pool, and the step runs on the calling thread.
/// </summary>
public sealed class Bepu3DEngine : PhysicsEngine3D
{
    private Bepu3DEngine()
    {
    }

    /// <summary>
    /// Gets the one instance of the engine.
    /// </summary>
    public static Bepu3DEngine Instance { get; } = new();

    /// <inheritdoc/>
    public override string Name => "bepu";

    internal override IPhysicsBackend3D CreateBackend(Vector3 gravity, float impactThreshold) =>
        new Bepu3DBackend(gravity, impactThreshold);
}
