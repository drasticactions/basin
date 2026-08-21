using Basin.Capabilities;

namespace Basin.XWayland;

public sealed class XWaylandToplevelSource : IToplevelSource, IDisposable
{
    private readonly XWaylandWm _wm;
    private readonly Dictionary<ulong, XWaylandWindow> _windows = [];
    private readonly Dictionary<XWaylandWindow, ulong> _ids = [];
    private ulong _nextId;

    public XWaylandToplevelSource(XWaylandWm wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        _wm = wm;
        _wm.WindowMapped += Track;
    }

    private readonly ToplevelObservers _observers = new();

    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    public XWaylandWindow? WindowFor(ulong localId) => _windows.GetValueOrDefault(localId);

    public ulong IdFor(XWaylandWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _ids.GetValueOrDefault(window);
    }

    public int Enumerate(Span<ToplevelInfo> toplevels)
    {
        if (_windows.Count > toplevels.Length)
        {
            return -1;
        }

        var written = 0;
        foreach (var (id, window) in _windows)
        {
            toplevels[written++] = Describe(id, window);
        }

        return written;
    }

    public bool TryGet(ulong localId, out ToplevelInfo info)
    {
        if (_windows.TryGetValue(localId, out var window))
        {
            info = Describe(localId, window);
            return true;
        }

        info = default;
        return false;
    }

    public bool Request(ulong localId, in ToplevelRequest request)
    {
        if (!_windows.TryGetValue(localId, out var window))
        {
            return false;
        }

        switch (request.Kind)
        {
            case ToplevelRequestKind.Activate:
                window.Activate();
                return true;
            case ToplevelRequestKind.Close:
                window.Close();
                return true;
            default:
                return false;
        }
    }

    public void Dispose() => _wm.WindowMapped -= Track;

    private void Track(XWaylandWindow window)
    {
        if (window.OverrideRedirect || _ids.ContainsKey(window))
        {
            return;
        }

        var id = ++_nextId;
        _windows[id] = window;
        _ids[window] = id;
        window.TitleChanged += () => _observers.Changed(id);
        window.GeometryChanged += () => _observers.Changed(id);
        window.Destroyed += () =>
        {
            _windows.Remove(id);
            _ids.Remove(window);
            _observers.Removed(id);
        };
        _observers.Added(id);
    }

    private static ToplevelInfo Describe(ulong id, XWaylandWindow window) =>
        new(
            id,
            window.Title,
            window.Class,
            window.IsMappedInX ? ToplevelState.None : ToplevelState.Minimized,
            window.Surface,
            new Box(window.X, window.Y, window.Width, window.Height));
}
