using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

/// <summary>
/// The window edges of a resize or a tile state. The values are the wire values of <c>xdg_toplevel.resize_edge</c>, and the bits combine like flags.
/// </summary>
public enum ResizeEdges
{
    /// <summary>
    /// No edge.
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
    /// The top and left edges.
    /// </summary>
    TopLeft = 5,
    /// <summary>
    /// The bottom and left edges.
    /// </summary>
    BottomLeft = 6,
    /// <summary>
    /// The right edge.
    /// </summary>
    Right = 8,
    /// <summary>
    /// The top and right edges.
    /// </summary>
    TopRight = 9,
    /// <summary>
    /// The bottom and right edges.
    /// </summary>
    BottomRight = 10,
}
