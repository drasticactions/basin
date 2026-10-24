namespace Basin.XWayland;

/// <summary>
/// Selects when an X11 window is ready for a <see cref="Transaction"/> after a configure.
/// </summary>
public enum XWaylandReadiness
{
    /// <summary>
    /// The window is ready at the first commit with a buffer of the configured size.
    /// </summary>
    OnMatchingCommit,

    /// <summary>
    /// The window is ready at the first commit with a buffer, of any size.
    /// </summary>
    OnNextCommit,

    /// <summary>
    /// The window does not join the transaction.
    /// </summary>
    Immediate,
}
