namespace Basin.Shell.Nested;

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
