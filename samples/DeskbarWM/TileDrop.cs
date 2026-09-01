using Basin.WindowManager;

namespace DeskbarWm;

internal readonly record struct TileDrop(ManagedWindow Target, Edges MovingEdge, int SnapPosition, Rect Region);
