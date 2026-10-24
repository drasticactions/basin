namespace Basin.WindowManager;

/// <summary>
/// The edges of a window. Combine values to name more than one edge, for example a corner for a resize.
/// </summary>
[Flags]
public enum Edges
{
    /// <summary>No edge.</summary>
    None = 0,

    /// <summary>The top edge.</summary>
    Top = 1,

    /// <summary>The bottom edge.</summary>
    Bottom = 2,

    /// <summary>The left edge.</summary>
    Left = 4,

    /// <summary>The right edge.</summary>
    Right = 8,

    /// <summary>All four edges.</summary>
    All = Top | Bottom | Left | Right,
}
