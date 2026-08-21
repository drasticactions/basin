using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

public interface IUISurfaceObserver
{
    void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage);

    void OnSurfaceDestroyed(IUISurface surface);
}
