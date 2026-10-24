using System.Numerics;

namespace Basin.Physics;

/// <summary>
/// Refers to one body in a <see cref="PhysicsWorld3D"/>. The handle is stale after the body is removed. Each member except <see cref="IsValid"/> then throws <see cref="InvalidOperationException"/>.
/// </summary>
/// <param name="World">The world that holds the body, or <see langword="null"/> for the default handle.</param>
/// <param name="Slot">The index of the body in the world.</param>
/// <param name="Generation">The generation of the slot, which makes a stale handle fail.</param>
public readonly record struct PhysicsBody3D(PhysicsWorld3D? World, int Slot, int Generation)
{
    /// <summary>
    /// Gets a value indicating whether the handle still refers to a live body in its world.
    /// </summary>
    public bool IsValid => World is { } world && world.IsLive(this);

    /// <summary>
    /// Gets the pose blended between the last two steps at the accumulator remainder. A consumer draws this pose.
    /// </summary>
    public PhysicsPose3D Pose => Live.Pose(this);

    /// <summary>
    /// Gets the pose at the end of the last step, with no blend.
    /// </summary>
    public PhysicsPose3D Current => Live.CurrentPose(this);

    /// <summary>
    /// Gets the linear velocity, in meters per second.
    /// </summary>
    public Vector3 Velocity => Live.VelocityOf(this).Linear;

    /// <summary>
    /// Gets the angular velocity, in radians per second about each world axis.
    /// </summary>
    public Vector3 AngularVelocity => Live.VelocityOf(this).Angular;

    /// <summary>
    /// Gets a value indicating whether the engine simulates the body. A sleeping body does not move until something wakes it.
    /// </summary>
    public bool IsAwake => Live.IsBodyAwake(this);

    /// <summary>
    /// Gets or sets a consumer value stored with the body. The world does not read it.
    /// </summary>
    public ulong Tag
    {
        get => Live.TagOf(this);
        set => Live.SetTag(this, value);
    }

    /// <summary>
    /// Gets the mass that the engine computes from the shape and the density.
    /// </summary>
    public double Mass => Live.MassOf(this);

    private PhysicsWorld3D Live => World ?? throw new InvalidOperationException("The physics body belongs to no world.");
}
