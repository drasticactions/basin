using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public enum LayerKind
{
    Background = 0,
    Bottom = 1,
    Top = 2,
    Overlay = 3,
}
