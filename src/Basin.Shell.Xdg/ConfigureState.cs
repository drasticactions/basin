using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public enum ConfigureState
{
    Idle,

    Inflight,

    Acked,

    Committed,

    TimedOut,
}
