using Wayland.Server;

namespace Basin.Capabilities;

/// <summary>
/// Stores the window state of <c>xdg_session_manager_v1</c> sessions so that it outlives the compositor process.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Creates and records a new session id.
    /// </summary>
    /// <returns>The new id, or <see langword="null"/> when the store does not create sessions. A client then gets a session that is not saved.</returns>
    string? CreateSessionId();

    /// <summary>
    /// Gets a value that tells whether a session id is known to the store.
    /// </summary>
    /// <param name="sessionId">The id that a client presents.</param>
    /// <returns><see langword="true"/> when the store holds the session, otherwise <see langword="false"/>.</returns>
    bool IsValidSessionId(string sessionId);

    /// <summary>
    /// Reads the saved state of one toplevel in a session.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="toplevelName">The name that the client gave the toplevel.</param>
    /// <param name="reason">The reason that the client gave for the restore.</param>
    /// <param name="state">The saved state, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the store holds a state for the toplevel, otherwise <see langword="false"/>.</returns>
    bool TryRestore(
        string sessionId,
        string toplevelName,
        SessionRestoreReason reason,
        out ToplevelSessionState state);

    /// <summary>
    /// Saves the state of one toplevel in a session, and replaces any earlier state.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="toplevelName">The name that the client gave the toplevel.</param>
    /// <param name="state">The state to save.</param>
    void Save(string sessionId, string toplevelName, in ToplevelSessionState state);

    /// <summary>
    /// Removes the saved state of one toplevel. An unknown name is ignored.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="toplevelName">The name of the toplevel to remove.</param>
    void ForgetToplevel(string sessionId, string toplevelName);

    /// <summary>
    /// Removes a session and all its toplevels. An unknown id is ignored.
    /// </summary>
    /// <param name="sessionId">The session id to remove.</param>
    void Forget(string sessionId);
}
