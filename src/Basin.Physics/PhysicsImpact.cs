namespace Basin.Physics;

/// <summary>
/// Describes one contact in a <see cref="PhysicsWorld"/> that began faster than <see cref="PhysicsWorld.ImpactThreshold"/>. Positions and speeds are in logical pixels.
/// </summary>
/// <param name="A">The first body of the pair. A wall contact names the wall body.</param>
/// <param name="B">The second body of the pair. A wall contact names the wall body.</param>
/// <param name="X">The horizontal position of the contact point.</param>
/// <param name="Y">The vertical position of the contact point.</param>
/// <param name="NormalX">The horizontal part of the unit normal, which points from <paramref name="A"/> toward <paramref name="B"/>.</param>
/// <param name="NormalY">The vertical part of the unit normal, which points from <paramref name="A"/> toward <paramref name="B"/>.</param>
/// <param name="Speed">The approach speed at the start of the contact, in pixels per second.</param>
public readonly record struct PhysicsImpact(
    PhysicsBody A,
    PhysicsBody B,
    double X,
    double Y,
    double NormalX,
    double NormalY,
    double Speed);
