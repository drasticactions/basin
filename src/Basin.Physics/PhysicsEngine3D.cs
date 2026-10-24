using System.Numerics;

namespace Basin.Physics;

/// <summary>
/// Names the engine that a <see cref="PhysicsWorld3D"/> runs on. Only the engine packages can derive from it.
/// </summary>
public abstract class PhysicsEngine3D
{
    private protected PhysicsEngine3D()
    {
    }

    /// <summary>
    /// Gets the short lowercase name of the engine, for logs and configuration.
    /// </summary>
    public abstract string Name { get; }

    internal abstract IPhysicsBackend3D CreateBackend(Vector3 gravity, float impactThreshold);
}
