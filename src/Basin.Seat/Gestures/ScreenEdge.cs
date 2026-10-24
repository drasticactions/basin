namespace Basin.Seat;

/// <summary>
/// One edge of an area. <see cref="ScreenEdges"/> is the flag set of the same edges.
/// </summary>
public enum ScreenEdge
{
    /// <summary>No edge.</summary>
    None,

    /// <summary>The left edge. The swipe moves to the right.</summary>
    Left,

    /// <summary>The right edge. The swipe moves to the left.</summary>
    Right,

    /// <summary>The top edge. The swipe moves down.</summary>
    Top,

    /// <summary>The bottom edge. The swipe moves up.</summary>
    Bottom,
}
