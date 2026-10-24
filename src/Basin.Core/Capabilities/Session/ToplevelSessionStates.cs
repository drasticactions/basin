using Wayland.Server;

namespace Basin.Capabilities;

/// <summary>
/// Lists the window states that a session saves. A combination means that each named state applies.
/// </summary>
[Flags]
public enum ToplevelSessionStates
{
    /// <summary>
    /// A normal floating window.
    /// </summary>
    None = 0,
    /// <summary>
    /// The window is maximized.
    /// </summary>
    Maximized = 1,
    /// <summary>
    /// The window is fullscreen.
    /// </summary>
    Fullscreen = 2,
    /// <summary>
    /// The left edge of the window is tiled.
    /// </summary>
    TiledLeft = 4,
    /// <summary>
    /// The right edge of the window is tiled.
    /// </summary>
    TiledRight = 8,
    /// <summary>
    /// The top edge of the window is tiled.
    /// </summary>
    TiledTop = 16,
    /// <summary>
    /// The bottom edge of the window is tiled.
    /// </summary>
    TiledBottom = 32,
}
