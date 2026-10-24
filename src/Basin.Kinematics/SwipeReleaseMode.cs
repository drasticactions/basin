namespace Basin.Kinematics;

/// <summary>
/// Names the rule that decides where a released swipe lands.
/// </summary>
public enum SwipeReleaseMode
{
    /// <summary>The release projects where the travel comes to rest and rounds it to a step. <see cref="SwipeProjection.Release"/> applies it.</summary>
    Projection,

    /// <summary>The release commits at a fraction of the travel or at a fling speed, and the recognizer decides it.</summary>
    Threshold,
}
