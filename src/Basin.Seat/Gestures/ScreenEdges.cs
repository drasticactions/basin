namespace Basin.Seat;

/// <summary>
/// A set of area edges. Combine values to enable edge swipes on more than one edge.
/// </summary>
[Flags]
public enum ScreenEdges
{
    /// <summary>No edge.</summary>
    None = 0,

    /// <summary>The left edge.</summary>
    Left = 1,

    /// <summary>The right edge.</summary>
    Right = 2,

    /// <summary>The top edge.</summary>
    Top = 4,

    /// <summary>The bottom edge.</summary>
    Bottom = 8,

    /// <summary>All four edges.</summary>
    All = Left | Right | Top | Bottom,
}
