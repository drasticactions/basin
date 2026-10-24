using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The stacking layer of a layer surface. The values are the wire values of <c>zwlr_layer_shell_v1.layer</c>, from bottom to top.
/// </summary>
public enum LayerKind
{
    /// <summary>
    /// The lowest layer, for example for a wallpaper.
    /// </summary>
    Background = 0,
    /// <summary>
    /// The layer above the background and below windows.
    /// </summary>
    Bottom = 1,
    /// <summary>
    /// The layer above windows, for example for a panel.
    /// </summary>
    Top = 2,
    /// <summary>
    /// The highest layer, above fullscreen windows.
    /// </summary>
    Overlay = 3,
}
