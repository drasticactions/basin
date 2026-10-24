using System.Numerics;

namespace Basin.Physics;

/// <summary>
/// Controls a capsule character in a <see cref="PhysicsWorld3D"/>. <see cref="PhysicsWorld3D.AddCharacter"/> makes it. The capsule is dynamic, so it pushes bodies and can stand on them.
/// </summary>
public sealed class PhysicsCharacter3D
{
    internal PhysicsCharacter3D(PhysicsWorld3D world, PhysicsBody3D body, float radius, float height)
    {
        World = world;
        Body = body;
        Radius = radius;
        Height = height;
    }

    /// <summary>
    /// Gets the world that holds the character.
    /// </summary>
    public PhysicsWorld3D World { get; }

    /// <summary>
    /// Gets the body of the capsule. <see cref="PhysicsWorld3D.Remove"/> on it removes the character.
    /// </summary>
    public PhysicsBody3D Body { get; }

    /// <summary>
    /// Gets the capsule radius, in meters.
    /// </summary>
    public float Radius { get; }

    /// <summary>
    /// Gets the full capsule height from the feet to the top, in meters.
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Gets the blended position of the capsule center. It throws <see cref="InvalidOperationException"/> after the body is removed.
    /// </summary>
    public Vector3 Position => Body.Pose.Position;

    /// <summary>
    /// Gets the linear velocity of the capsule, in meters per second.
    /// </summary>
    public Vector3 Velocity => Body.Velocity;

    /// <summary>
    /// Gets a value indicating whether the capsule stands on a support after the last step. It is <see langword="false"/> after the body is removed.
    /// </summary>
    public bool Supported => World.IsSupported(this);

    /// <summary>
    /// Sets the target horizontal velocity of the character. The goal stays until the next call.
    /// </summary>
    /// <param name="wish">The target velocity in meters per second. Y is forward along the view and X is to the right.</param>
    /// <param name="view">The view direction in world space that gives forward.</param>
    public void Move(Vector2 wish, Vector3 view) => World.SetCharacterGoal(this, wish, view, jump: false);

    /// <summary>
    /// Requests a jump on the next step. The character jumps only when it is supported.
    /// </summary>
    public void Jump() => World.RequestJump(this);

    /// <summary>
    /// Moves the capsule center to a position with no sweep, and clears its rotation. It wakes the body.
    /// </summary>
    /// <param name="position">The new center position in world space.</param>
    public void Teleport(Vector3 position) => World.Teleport(Body, PhysicsPose3D.At(position));
}
