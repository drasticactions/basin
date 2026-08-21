using Basin.Scene;
using Basin.Shell.River.Protocol;
using Basin.Shell.Xdg;
using Basin.XWayland;

namespace Basin.Shell.River;

internal enum WindowPhase
{
    Init,

    Ready,

    Initialized,

    Mapped,

    Closing,
}
