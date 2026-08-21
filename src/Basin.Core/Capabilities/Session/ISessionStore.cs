using Wayland.Server;

namespace Basin.Capabilities;

public interface ISessionStore
{
    string? CreateSessionId();

    bool IsValidSessionId(string sessionId);

    bool TryRestore(
        string sessionId,
        string toplevelName,
        SessionRestoreReason reason,
        out ToplevelSessionState state);

    void Save(string sessionId, string toplevelName, in ToplevelSessionState state);

    void ForgetToplevel(string sessionId, string toplevelName);

    void Forget(string sessionId);
}
