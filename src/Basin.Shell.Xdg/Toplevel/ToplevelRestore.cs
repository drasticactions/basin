using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

/// <summary>
/// A saved session state that a session manager applies to a toplevel with <see cref="XdgToplevelWindow.Restore"/>.
/// </summary>
/// <param name="SessionId">The identifier of the session.</param>
/// <param name="Name">The name of the toplevel in the session.</param>
/// <param name="Reason">Why the session is restored.</param>
/// <param name="State">The saved geometry and states. The position in it is for the consumer to apply.</param>
public readonly record struct ToplevelRestore(
    string SessionId,
    string Name,
    SessionRestoreReason Reason,
    ToplevelSessionState State);
