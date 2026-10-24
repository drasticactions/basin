using System.Numerics;

namespace Basin.Physics;

/// <summary>
/// Describes one contact in a <see cref="PhysicsWorld3D"/> that began faster than <see cref="PhysicsWorld3D.ImpactThreshold"/>. All values are in meters.
/// </summary>
/// <param name="A">The first body of the pair. It is <see langword="default"/> when the contact is with a static shape.</param>
/// <param name="B">The second body of the pair. It is <see langword="default"/> when the contact is with a static shape.</param>
/// <param name="Point">The contact point in world space.</param>
/// <param name="Normal">The unit contact normal, from <paramref name="A"/> toward <paramref name="B"/>.</param>
/// <param name="Speed">The approach speed along the normal, in meters per second.</param>
public readonly record struct PhysicsImpact3D(
    PhysicsBody3D A,
    PhysicsBody3D B,
    Vector3 Point,
    Vector3 Normal,
    double Speed);
