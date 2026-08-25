using Pixman;

namespace Basin.Capabilities;

public interface IBackgroundContrast
{
    bool TryGetContrast(Surface surface, out ContrastParameters parameters);

    PixmanRegion32? ContrastRegionOf(Surface surface);
}
