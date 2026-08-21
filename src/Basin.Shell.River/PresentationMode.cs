using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.River.Protocol;
using Basin.Shell.Xdg;
using Basin.XWayland;
using Wayland.Server;

namespace Basin.Shell.River;

public enum PresentationMode
{
    Vsync = 0,

    Async = 1,
}
