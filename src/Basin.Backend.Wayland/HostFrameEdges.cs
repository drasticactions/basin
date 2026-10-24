using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Basin.Protocol;
using Pixman;
using Wayland;

namespace Basin.Backend.Wayland;

/// <summary>
/// Names the edges of a host frame that an interactive resize moves. The values match the xdg-toplevel resize edges, so a corner is the sum of its two sides.
/// </summary>
public enum HostFrameEdges
{
    /// <summary>No edge. <see cref="WaylandHostFrame.StartResize(HostFrameEdges)"/> ignores this value.</summary>
    None = 0,
    /// <summary>The top edge.</summary>
    Top = 1,
    /// <summary>The bottom edge.</summary>
    Bottom = 2,
    /// <summary>The left edge.</summary>
    Left = 4,
    /// <summary>The top-left corner.</summary>
    TopLeft = 5,
    /// <summary>The bottom-left corner.</summary>
    BottomLeft = 6,
    /// <summary>The right edge.</summary>
    Right = 8,
    /// <summary>The top-right corner.</summary>
    TopRight = 9,
    /// <summary>The bottom-right corner.</summary>
    BottomRight = 10,
}
