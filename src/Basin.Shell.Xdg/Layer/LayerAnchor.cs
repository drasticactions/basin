using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

[Flags]
public enum LayerAnchor
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8,
}
