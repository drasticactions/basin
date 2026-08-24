using Basin;

namespace Westonia;

internal sealed partial class Westonia
{
    private readonly List<SurfaceBox> _presence = [];
    private SurfacePresenceTracker _presenceTracker = null!;

    private void UpdateSurfacePresence()
    {
        if (_fractionalScale is null)
        {
            return;
        }

        _scene.CollectSurfaces(_presence);
        _presenceTracker.Update(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_presence));
        _presence.Clear();
    }
}
