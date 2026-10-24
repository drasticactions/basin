using Wayland;

namespace Basin.Seat;

/// <summary>
/// A cursor that the focused client asked for with <c>wl_pointer.set_cursor</c>.
/// </summary>
/// <param name="Surface">The cursor surface, or <see langword="null"/> to hide the cursor.</param>
/// <param name="HotspotX">The horizontal hotspot in logical pixels.</param>
/// <param name="HotspotY">The vertical hotspot in logical pixels.</param>
public readonly record struct CursorRequest(Surface? Surface, int HotspotX, int HotspotY);
