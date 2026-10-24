namespace Basin;

/// <summary>
/// Selects the corners of a rectangle that get a rounded shape. A combination of flags rounds each corner that it names.
/// </summary>
[Flags]
public enum RoundedCorners
{
    /// <summary>
    /// No corner is rounded.
    /// </summary>
    None = 0,
    /// <summary>
    /// The top-left corner.
    /// </summary>
    TopLeft = 1,
    /// <summary>
    /// The top-right corner.
    /// </summary>
    TopRight = 2,
    /// <summary>
    /// The bottom-left corner.
    /// </summary>
    BottomLeft = 4,
    /// <summary>
    /// The bottom-right corner.
    /// </summary>
    BottomRight = 8,
    /// <summary>
    /// The two top corners.
    /// </summary>
    Top = TopLeft | TopRight,
    /// <summary>
    /// The two bottom corners.
    /// </summary>
    Bottom = BottomLeft | BottomRight,
    /// <summary>
    /// All four corners.
    /// </summary>
    All = Top | Bottom,
}
