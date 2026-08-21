using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class XdgToplevelSource : IToplevelSource, IDisposable
{
    private readonly XdgShell _shell;
    private readonly Dictionary<ulong, XdgToplevelWindow> _windows = [];
    private readonly Dictionary<XdgToplevelWindow, ulong> _ids = [];
    private readonly Dictionary<ulong, Box> _geometry = [];
    private ulong _nextId;

    public XdgToplevelSource(XdgShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        _shell.NewToplevel += Track;
    }

    private readonly ToplevelObservers _observers = new();

    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    public event Action<XdgToplevelWindow>? ActivateRequested;

    public XdgToplevelWindow? WindowFor(ulong localId) => _windows.GetValueOrDefault(localId);

    public ulong IdFor(XdgToplevelWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _ids.GetValueOrDefault(window);
    }

    public void SetGeometry(XdgToplevelWindow window, in Box geometry)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_ids.TryGetValue(window, out var id))
        {
            if (_geometry.TryGetValue(id, out var current) && current == geometry)
            {
                return;
            }

            _geometry[id] = geometry;
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
                ActivateRequested?.Invoke(window);
                return true;
            case ToplevelRequestKind.Close:
                window.Close();
                return true;
            case ToplevelRequestKind.Maximize:
                window.SetMaximized(true);
                window.RequestConfigure();
                return true;
            case ToplevelRequestKind.Unmaximize:
                window.SetMaximized(false);
                window.RequestConfigure();
                return true;
            case ToplevelRequestKind.Fullscreen:
                window.SetFullscreen(true);
                window.RequestConfigure();
                return true;
            case ToplevelRequestKind.Unfullscreen:
                window.SetFullscreen(false);
                window.RequestConfigure();
                return true;
            default:
                return false;
        }
    }

    public void Dispose() => _shell.NewToplevel -= Track;

    private void Track(XdgToplevelWindow window)
    {
        var id = ++_nextId;
        _windows[id] = window;
        _ids[window] = id;
        window.TitleChanged += () => _observers.Changed(id);
        window.AppIdChanged += () => _observers.Changed(id);
        window.Destroyed += () =>
        {
            _windows.Remove(id);
            _ids.Remove(window);
            _geometry.Remove(id);
            _observers.Removed(id);
        };
        _observers.Added(id);
    }

    private ToplevelInfo Describe(ulong id, XdgToplevelWindow window)
    {
        var state = ToplevelState.None;
        if (window.HasState(Protocol.XdgToplevel.State.Maximized))
        {
            state |= ToplevelState.Maximized;
        }

        if (window.HasState(Protocol.XdgToplevel.State.Fullscreen))
        {
            state |= ToplevelState.Fullscreen;
        }

        if (window.HasState(Protocol.XdgToplevel.State.Activated))
        {
            state |= ToplevelState.Activated;
        }

        return new ToplevelInfo(
            id,
            window.Title,
            window.AppId,
            state,
            window.Surface,
            _geometry.GetValueOrDefault(id));
    }
}
