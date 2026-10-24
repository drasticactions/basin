namespace Basin.Shell.Nested;

/// <summary>
/// The input to <see cref="Placement.Place"/>. All boxes and points are in logical layout coordinates.
/// </summary>
/// <param name="Width">The frame width of the new window, borders included.</param>
/// <param name="Height">The frame height of the new window, title bar included.</param>
/// <param name="WorkArea">The output area that the panel strips do not cover.</param>
/// <param name="Visible">The frames of the other visible windows on the current workspace.</param>
/// <param name="Pointer">The pointer position.</param>
/// <param name="Mode">The placement rule to apply.</param>
/// <param name="CenterNewWindows">A value indicating whether a window that fits nowhere is centered instead of cascaded.</param>
/// <param name="ParentFrame">The frame of the parent window, or <see langword="null"/> for a window with no mapped parent.</param>
/// <param name="ParentTitleHeight">The title bar height of the parent window.</param>
/// <param name="Output">The full output box.</param>
public readonly record struct PlacementRequest(
    int Width,
    int Height,
    Box WorkArea,
    IReadOnlyList<Box> Visible,
    Point Pointer,
    PlacementMode Mode,
    bool CenterNewWindows,
    Box? ParentFrame,
    int ParentTitleHeight,
    Box Output);
