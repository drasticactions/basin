using Wayland.Server;

namespace Basin.Capabilities;

[Flags]
public enum ToplevelSessionStates
{
    None = 0,
    Maximized = 1,
    Fullscreen = 2,
    TiledLeft = 4,
    TiledRight = 8,
    TiledTop = 16,
    TiledBottom = 32,
}
