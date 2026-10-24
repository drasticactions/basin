namespace Basin.Shell.Nested;

/// <summary>
/// The edge of the work area that a window is tiled against.
/// </summary>
public enum TileEdge
{
    /// <summary>
    /// The window is not tiled.
    /// </summary>
    None,
    /// <summary>
    /// The window fills the left half of the work area.
    /// </summary>
    Left,
    /// <summary>
    /// The window fills the right half of the work area.
    /// </summary>
    Right,
    /// <summary>
    /// The window is maximized. <see cref="NestedShell.SetTile"/> maximizes for this value.
    /// </summary>
    Top,
}
