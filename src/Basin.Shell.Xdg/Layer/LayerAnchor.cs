using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The output edges that a layer surface is anchored to. A combination anchors the surface to each edge in it. The values are the wire values of <c>zwlr_layer_surface_v1.anchor</c>.
/// </summary>
[Flags]
public enum LayerAnchor
{
    /// <summary>
    /// No edge. The surface is centered on the output.
    /// </summary>
    None = 0,
    /// <summary>
    /// The top edge.
    /// </summary>
    Top = 1,
    /// <summary>
    /// The bottom edge.
    /// </summary>
    Bottom = 2,
    /// <summary>
    /// The left edge.
    /// </summary>
    Left = 4,
    /// <summary>
    /// The right edge.
    /// </summary>
    Right = 8,
}
