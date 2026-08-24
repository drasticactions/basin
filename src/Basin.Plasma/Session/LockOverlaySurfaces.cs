using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class LockOverlaySurfaces : ILockOverlaySurfaces
{
    private readonly HashSet<Surface> _allowed = [];

    public bool IsAllowed(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return _allowed.Contains(surface);
    }

    internal void Allow(Surface surface)
    {
        if (_allowed.Add(surface))
        {
            surface.Destroyed += () => _allowed.Remove(surface);
        }
    }
}
