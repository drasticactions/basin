using Wayland.Server;

namespace Basin.Capabilities;

/// <summary>
/// Lists the reasons that a client gives when it restores a session, with the values of <c>xdg_session_manager_v1.reason</c>.
/// </summary>
public enum SessionRestoreReason
{
    /// <summary>
    /// The user started the application again.
    /// </summary>
    Launch = 1,

    /// <summary>
    /// The application restarts after a crash or a compositor restart.
    /// </summary>
    Recover = 2,

    /// <summary>
    /// The desktop session restores its applications, for example after a log-in.
    /// </summary>
    SessionRestore = 3,
}
