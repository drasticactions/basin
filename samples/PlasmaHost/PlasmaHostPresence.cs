using Basin;

namespace PlasmaHost;

internal sealed partial class PlasmaHost
{
    private readonly List<SurfaceBox> _presence = [];
    private SurfacePresenceTracker _presenceTracker = null!;

    private void UpdateSurfacePresence()
    {
        _scene.CollectSurfaces(_presence);
        _presenceTracker.Update(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_presence));
        _presence.Clear();
    }
}
