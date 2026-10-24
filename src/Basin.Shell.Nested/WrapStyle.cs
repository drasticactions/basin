namespace Basin.Shell.Nested;

/// <summary>
/// Selects what <see cref="Workspaces.Neighbor"/> does at the edge of the workspace grid.
/// </summary>
public enum WrapStyle
{
    /// <summary>
    /// The move stops at the edge of the grid.
    /// </summary>
    NoWrap,
    /// <summary>
    /// The move wraps to the opposite edge and steps to the next row or column, as reading order does.
    /// </summary>
    Classic,
    /// <summary>
    /// The move wraps to the opposite edge of the same row or column.
    /// </summary>
    Toroidal,
}
