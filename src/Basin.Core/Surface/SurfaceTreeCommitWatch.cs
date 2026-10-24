namespace Basin;

/// <summary>
/// Reports the damage of each commit in a surface tree, in the root's coordinates. It follows subsurfaces as they are added.
/// </summary>
public sealed class SurfaceTreeCommitWatch : IDisposable
{
    private readonly Surface _root;
    private readonly Action<Box> _committed;
    private readonly Dictionary<Surface, Entry> _entries = [];
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="SurfaceTreeCommitWatch"/> class and watches the root and its current subsurfaces.</summary>
    /// <param name="root">The root surface.</param>
    /// <param name="committed">Called after a commit with non-empty damage, with the damage extents relative to the root.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> or <paramref name="committed"/> is <see langword="null"/>.</exception>
    public SurfaceTreeCommitWatch(Surface root, Action<Box> committed)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(committed);
        _root = root;
        _committed = committed;
        Watch(root);
        Reconcile(root);
    }

    /// <summary>Gets the number of surfaces that the watch hooks.</summary>
    public int WatchedSurfaces => _entries.Count;

    /// <summary>Unhooks every surface. A second call does nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _entries.Values)
        {
            entry.Detach();
        }

        _entries.Clear();
    }

    private void Watch(Surface surface)
    {
        if (!_entries.ContainsKey(surface))
        {
            _entries[surface] = new Entry(this, surface);
        }
    }

    private void Reconcile(Surface surface)
    {
        var below = surface.SubsurfacesBelow;
        for (var i = 0; i < below.Count; i++)
        {
            Watch(below[i].Surface);
            Reconcile(below[i].Surface);
        }

        var above = surface.SubsurfacesAbove;
        for (var i = 0; i < above.Count; i++)
        {
            Watch(above[i].Surface);
            Reconcile(above[i].Surface);
        }
    }

    private void OnCommitted(Surface surface)
    {
        if (_disposed)
        {
            return;
        }

        Reconcile(surface);
        var damage = surface.CommittedDamageExtents();
        if (damage.IsEmpty)
        {
            return;
        }

        for (var candidate = surface; candidate != _root && candidate.SubsurfaceRole is { } role;)
        {
            damage = damage.Translated(role.X, role.Y);
            candidate = role.Parent;
        }

        _committed(damage);
    }

    private void OnDestroyed(Surface surface)
    {
        if (_entries.Remove(surface, out var entry))
        {
            entry.Detach();
        }
    }

    private sealed class Entry
    {
        private readonly SurfaceTreeCommitWatch _watch;
        private readonly Surface _surface;
        private readonly Action _committed;
        private readonly Action _destroyed;

        public Entry(SurfaceTreeCommitWatch watch, Surface surface)
        {
            _watch = watch;
            _surface = surface;
            _committed = () => _watch.OnCommitted(_surface);
            _destroyed = () => _watch.OnDestroyed(_surface);
            surface.Committed += _committed;
            surface.Destroyed += _destroyed;
        }

        public void Detach()
        {
            _surface.Committed -= _committed;
            _surface.Destroyed -= _destroyed;
        }
    }
}
