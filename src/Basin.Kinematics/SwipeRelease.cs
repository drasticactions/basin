namespace Basin.Kinematics;

/// <summary>
/// The landing of a released swipe, from <see cref="SwipeProjection.Release"/>.
/// </summary>
/// <param name="Target">The step on which the swipe lands.</param>
/// <param name="Position">The position on screen at the release, in steps, after the rubber band.</param>
/// <param name="Velocity">The speed for the settle spring, in steps per second. It is the speed on screen at the release.</param>
public readonly record struct SwipeRelease(int Target, double Position, double Velocity);
