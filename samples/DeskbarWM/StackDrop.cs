using Basin.WindowManager;

namespace DeskbarWm;

internal readonly record struct StackDrop(ManagedWindow Target, Rect Region);
