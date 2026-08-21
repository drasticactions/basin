using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin;

public readonly record struct CursorImage(IBuffer Buffer, int HotspotX, int HotspotY, bool Clipped = false);
