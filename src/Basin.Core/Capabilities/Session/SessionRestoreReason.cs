using Wayland.Server;

namespace Basin.Capabilities;

public enum SessionRestoreReason
{
    Launch = 1,

    Recover = 2,

    SessionRestore = 3,
}
