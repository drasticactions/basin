namespace Basin.Capabilities;

/// <summary>
/// The edges of a resize. The values match the <c>xdg_toplevel.resize_edge</c> wire values, and a corner is the sum of its two edges.
/// </summary>
public enum FrameEdges
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
    /// The top-left corner.
    /// </summary>
    TopLeft = 5,
    /// <summary>
    /// The bottom-left corner.
    /// </summary>
    BottomLeft = 6,
    /// <summary>
    /// The right edge.
    /// </summary>
    Right = 8,
    /// <summary>
    /// The top-right corner.
    /// </summary>
    TopRight = 9,
    /// <summary>
    /// The bottom-right corner.
    /// </summary>
    BottomRight = 10,
}
