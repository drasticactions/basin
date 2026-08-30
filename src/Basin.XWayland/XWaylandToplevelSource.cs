using Basin.Capabilities;

namespace Basin.XWayland;

public sealed class XWaylandToplevelSource : IToplevelSource, IDisposable
{
    private readonly XWaylandWm _wm;
    private readonly Dictionary<ulong, XWaylandWindow> _windows = [];
    private readonly Dictionary<XWaylandWindow, ulong> _ids = [];
    private readonly Dictionary<ulong, (Box Frame, Box Client)> _geometry = [];
    private readonly Dictionary<ulong, ToplevelState> _flags = [];
    private readonly Dictionary<ulong, uint> _pids = [];
    private readonly Dictionary<ulong, Box> _minimized = [];
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

    public event Action<XWaylandWindow, bool>? NoBorderRequested;

    public event Action<XWaylandWindow, bool>? CaptureExclusionRequested;

    public event Action<XWaylandWindow, Surface?, Box>? MinimizedGeometryRequested;

    public XWaylandWindow? WindowFor(ulong localId) => _windows.GetValueOrDefault(localId);

    public ulong IdFor(XWaylandWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _ids.GetValueOrDefault(window);
    }

    public void SetGeometry(XWaylandWindow window, in Box frame, in Box client)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_ids.TryGetValue(window, out var id))
        {
            if (_geometry.TryGetValue(id, out var current) && current.Frame == frame && current.Client == client)
            {
                return;
            }

            _geometry[id] = (frame, client);
            _observers.Changed(id);
        }
    }

    public void SetMinimizedGeometry(XWaylandWindow window, in Box geometry)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_ids.TryGetValue(window, out var id))
        {
            if (_minimized.GetValueOrDefault(id) == geometry)
            {
                return;
            }

            if (geometry.IsEmpty)
            {
                _minimized.Remove(id);
            }
            else
            {
                _minimized[id] = geometry;
            }

            _observers.Changed(id);
        }
    }

    public void SetDecoration(XWaylandWindow window, bool noBorder, bool userCanSet)
    {
        ArgumentNullException.ThrowIfNull(window);
        var flags = (noBorder ? ToplevelState.NoBorder : ToplevelState.None) |
            (userCanSet ? ToplevelState.CanSetNoBorder : ToplevelState.None);
        UpdateFlags(window, ToplevelState.NoBorder | ToplevelState.CanSetNoBorder, flags);
    }

    public void SetExcludedFromCapture(XWaylandWindow window, bool excluded)
    {
        ArgumentNullException.ThrowIfNull(window);
        UpdateFlags(
            window,
            ToplevelState.ExcludedFromCapture,
            excluded ? ToplevelState.ExcludedFromCapture : ToplevelState.None);
    }

    private void UpdateFlags(XWaylandWindow window, ToplevelState mask, ToplevelState value)
    {
        if (_ids.TryGetValue(window, out var id))
        {
            var current = _flags.GetValueOrDefault(id);
            var next = (current & ~mask) | value;
            if (next == current)
            {
                return;
            }

            _flags[id] = next;
            _observers.Changed(id);
        }
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
            case ToplevelRequestKind.Minimize or ToplevelRequestKind.Unminimize:
                window.RaiseMinimizeRequested(request.Kind == ToplevelRequestKind.Minimize);
                return true;
            case ToplevelRequestKind.SetNoBorder or ToplevelRequestKind.UnsetNoBorder
                when NoBorderRequested is { } noBorder:
                noBorder.Invoke(window, request.Kind == ToplevelRequestKind.SetNoBorder);
                return true;
            case ToplevelRequestKind.SetMinimizedGeometry or ToplevelRequestKind.UnsetMinimizedGeometry
                when MinimizedGeometryRequested is { } minimizedGeometry:
                minimizedGeometry.Invoke(
                    window,
                    request.Surface,
                    request.Kind == ToplevelRequestKind.SetMinimizedGeometry ? request.Geometry : default);
                return true;
            case ToplevelRequestKind.ExcludeFromCapture or ToplevelRequestKind.IncludeInCapture
                when CaptureExclusionRequested is { } exclusion:
                exclusion.Invoke(window, request.Kind == ToplevelRequestKind.ExcludeFromCapture);
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
        window.PropertiesChanged += () => _observers.Changed(id);
        window.MinimizeRequested += _ => _observers.Changed(id);
        window.Destroyed += () =>
        {
            _windows.Remove(id);
            _ids.Remove(window);
            _geometry.Remove(id);
            _flags.Remove(id);
            _pids.Remove(id);
            _minimized.Remove(id);
            _observers.Removed(id);
        };
        _observers.Added(id);
    }

    private ToplevelInfo Describe(ulong id, XWaylandWindow window)
    {
        var rect = new Box(window.X, window.Y, window.Width, window.Height);
        var (frame, client) = _geometry.TryGetValue(id, out var pair) ? pair : (rect, rect);
        var state = _flags.GetValueOrDefault(id);
        if (window.Minimized || !window.IsMappedInX)
        {
            state |= ToplevelState.Minimized;
        }

        if (!_pids.TryGetValue(id, out var pid) && window.Surface is { } surface)
        {
            pid = surface.Resource.Client.TryGetCredentials(out var credentials) ? (uint)credentials.Pid : 0;
            _pids[id] = pid;
        }

        return new(
            id,
            window.Title,
            window.Class,
            state,
            window.Surface,
            frame,
            client,
            window.Instance,
            pid,
            window.TransientFor is { } transient ? _ids.GetValueOrDefault(transient) : 0,
            MinimizedGeometry: _minimized.GetValueOrDefault(id));
    }
}
