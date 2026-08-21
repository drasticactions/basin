using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin;

public readonly record struct CursorImage(
    IBuffer Buffer,
    int Width,
    int Height,
    int HotspotX,
    int HotspotY,
    bool Clipped = false);
