using Basin.Capabilities;
using Pixman;

namespace Basin.Scene;

public readonly record struct UIPointerRoute(IUISurface? Surface, bool Entered, string? Cursor);
