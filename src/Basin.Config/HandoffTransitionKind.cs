namespace Basin.Config;

/// <summary>
/// The kinds of transition from an inherited frame to the first frame of the new compositor.
/// </summary>
public enum HandoffTransitionKind
{
    /// <summary>
    /// The inherited frame fades out over the new frames.
    /// </summary>
    Crossfade,
    /// <summary>
    /// The first new frame replaces the inherited frame with no fade.
    /// </summary>
    Cut,
}
