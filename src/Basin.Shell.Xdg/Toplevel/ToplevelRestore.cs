using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

public readonly record struct ToplevelRestore(
    string SessionId,
    string Name,
    SessionRestoreReason Reason,
    ToplevelSessionState State);
