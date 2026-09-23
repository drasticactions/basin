using Pixman;

namespace Basin.Scene;

public interface ISurfaceBackdrop
{
    IBackdropEffect? Effect { get; }

    bool RegionOf(Surface surface, PixmanRegion32 into);

    void Forget(object key);
}
