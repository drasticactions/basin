using Pixman;

namespace Basin.Capabilities.Defaults;

public sealed class DefaultSurfaceAppearance : ISurfaceAppearance
{
    private readonly Dictionary<Surface, Entry> _entries = [];
    private readonly SurfaceAppearanceObservers _observers = new();

    private sealed class Entry
    {
        public double Opacity = 1.0;
        public PixmanRegion32? Visible;
        public Action? OnDestroyed;
    }

    public double OpacityOf(Surface surface) =>
        _entries.TryGetValue(surface, out var entry) ? entry.Opacity : 1.0;

    public bool TryVisibleRegion(Surface surface, out PixmanRegion32 region)
    {
        if (_entries.TryGetValue(surface, out var entry) && entry.Visible is { IsEmpty: false } visible)
        {
            region = visible;
            return true;
        }

        region = null!;
        return false;
    }

    public void SetOpacity(Surface surface, double opacity)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var clamped = Math.Clamp(opacity, 0.0, 1.0);
        if (!_entries.TryGetValue(surface, out var entry))
        {
            if (clamped >= 1.0)
            {
                return;
            }

            entry = Track(surface);
        }
        else if (entry.Opacity == clamped)
        {
            return;
        }

        entry.Opacity = clamped;
        Prune(surface, entry);
        _observers.Changed(surface);
    }

    public void SetVisibleRegion(Surface surface, PixmanRegion32 region)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(region);
        if (region.IsEmpty)
        {
            ClearVisibleRegion(surface);
            return;
        }

        if (!_entries.TryGetValue(surface, out var entry))
        {
            entry = Track(surface);
        }

        if (entry.Visible is { } existing && existing.Equals(region))
        {
            return;
        }

        entry.Visible ??= new PixmanRegion32();
        entry.Visible.Copy(region);
        _observers.Changed(surface);
    }

    public void ClearVisibleRegion(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!_entries.TryGetValue(surface, out var entry) || entry.Visible is not { IsEmpty: false } visible)
        {
            return;
        }

        visible.Dispose();
        entry.Visible = null;
        Prune(surface, entry);
        _observers.Changed(surface);
    }

    public void AddObserver(ISurfaceAppearanceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(ISurfaceAppearanceObserver observer) => _observers.Remove(observer);

    private Entry Track(Surface surface)
    {
        var entry = new Entry();
        entry.OnDestroyed = () => Forget(surface);
        surface.Destroyed += entry.OnDestroyed;
        _entries[surface] = entry;
        return entry;
    }

    private void Prune(Surface surface, Entry entry)
    {
        if (entry.Opacity < 1.0 || entry.Visible is not null)
        {
            return;
        }

        Forget(surface);
    }

    private void Forget(Surface surface)
    {
        if (!_entries.Remove(surface, out var entry))
        {
            return;
        }

        if (entry.OnDestroyed is { } handler)
        {
            surface.Destroyed -= handler;
        }

        entry.Visible?.Dispose();
    }
}
