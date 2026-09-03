using Pixman;

namespace Basin.Capabilities;

public interface ISurfaceAppearance
{
    double OpacityOf(Surface surface);

    bool TryVisibleRegion(Surface surface, out PixmanRegion32 region);

    void SetOpacity(Surface surface, double opacity);

    void SetVisibleRegion(Surface surface, PixmanRegion32 region);

    void ClearVisibleRegion(Surface surface);

    void AddObserver(ISurfaceAppearanceObserver observer);

    void RemoveObserver(ISurfaceAppearanceObserver observer);
}
