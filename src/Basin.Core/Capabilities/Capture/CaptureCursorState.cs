namespace Basin.Capabilities;

public readonly record struct CaptureCursorState(
    int X,
    int Y,
    int HotspotX,
    int HotspotY,
    int Width,
    int Height,
    bool IsVisible);
