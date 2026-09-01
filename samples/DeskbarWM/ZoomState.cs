using Basin.WindowManager;

namespace DeskbarWm;

internal sealed class ZoomState(Rect restore)
{
    public Rect Restore { get; } = restore;
}
