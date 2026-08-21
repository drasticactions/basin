using Wayland;

namespace Basin.Seat;

public readonly record struct CursorRequest(Surface? Surface, int HotspotX, int HotspotY);
