namespace Basin.Config;

/// <summary>
/// The values of the <c>readiness</c> quirk. They set when an X11 window is ready in a configure transaction.
/// </summary>
public enum ReadinessQuirk
{
    /// <summary>
    /// The window is ready at the first commit with a buffer of the configured size.
    /// </summary>
    Matching,
    /// <summary>
    /// The window does not join the transaction and is ready at once.
    /// </summary>
    Immediate,
}
