using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

[Flags]
public enum XdgWmCapabilities
{
    None = 0,
    WindowMenu = 1,
    Maximize = 2,
    Fullscreen = 4,
    Minimize = 8,
    All = WindowMenu | Maximize | Fullscreen | Minimize,
}
