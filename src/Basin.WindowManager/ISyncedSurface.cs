using Wayland;

namespace Basin.WindowManager;

internal interface ISyncedSurface
{
    WlSurface Surface { get; }
}
