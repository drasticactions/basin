namespace Basin.Capabilities;

/// <summary>
/// Describes the cursor image and its position for capture.
/// </summary>
/// <param name="X">The X position of the hotspot. <see cref="IScreenCapture.SetCursor"/> takes layout coordinates and <see cref="IScreenCapture.TryCursorState"/> returns output buffer pixels.</param>
/// <param name="Y">The Y position of the hotspot, in the same units as <paramref name="X"/>.</param>
/// <param name="HotspotX">The X offset of the hotspot in the cursor image, in buffer pixels.</param>
/// <param name="HotspotY">The Y offset of the hotspot in the cursor image, in buffer pixels.</param>
/// <param name="Width">The width of the cursor image in buffer pixels.</param>
/// <param name="Height">The height of the cursor image in buffer pixels.</param>
/// <param name="IsVisible">A value indicating whether a cursor image is shown.</param>
public readonly record struct CaptureCursorState(
    int X,
    int Y,
    int HotspotX,
    int HotspotY,
    int Width,
    int Height,
    bool IsVisible);
