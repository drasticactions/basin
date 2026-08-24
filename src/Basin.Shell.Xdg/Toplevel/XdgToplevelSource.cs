using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class XdgToplevelSource : IToplevelSource, IDisposable
{
    private readonly XdgShell _shell;
    private readonly Dictionary<ulong, XdgToplevelWindow> _windows = [];
    private readonly Dictionary<XdgToplevelWindow, ulong> _ids = [];
    private readonly Dictionary<ulong, (Box Frame, Box Client)> _geometry = [];
    private readonly Dictionary<ulong, ToplevelState> _flags = [];
    private readonly Dictionary<ulong, uint> _pids = [];
    private readonly Dictionary<ulong, (string Service, string ObjectPath)> _appMenus = [];
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

    public event Action<XdgToplevelWindow, bool>? NoBorderRequested;

    public event Action<XdgToplevelWindow, bool>? MinimizeRequested;

    public event Action<XdgToplevelWindow, bool>? CaptureExclusionRequested;

    public XdgToplevelWindow? WindowFor(ulong localId) => _windows.GetValueOrDefault(localId);

    public ulong IdFor(XdgToplevelWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _ids.GetValueOrDefault(window);
    }

    public void SetGeometry(XdgToplevelWindow window, in Box frame, in Box client)
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

    public void SetDecoration(XdgToplevelWindow window, bool noBorder, bool userCanSet)
    {
        ArgumentNullException.ThrowIfNull(window);
        var flags = (noBorder ? ToplevelState.NoBorder : ToplevelState.None) |
            (userCanSet ? ToplevelState.CanSetNoBorder : ToplevelState.None);
        UpdateFlags(window, ToplevelState.NoBorder | ToplevelState.CanSetNoBorder, flags);
    }

    public void SetAppMenu(XdgToplevelWindow window, string serviceName, string objectPath)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(objectPath);
        if (_ids.TryGetValue(window, out var id))
        {
            var next = (serviceName, objectPath);
            if (_appMenus.GetValueOrDefault(id) == next)
            {
                return;
            }

            _appMenus[id] = next;
            _observers.Changed(id);
        }
    }

    public void SetSkipTaskbar(XdgToplevelWindow window, bool skip)
    {
        ArgumentNullException.ThrowIfNull(window);
        UpdateFlags(window, ToplevelState.SkipTaskbar, skip ? ToplevelState.SkipTaskbar : ToplevelState.None);
    }

    public void SetSkipSwitcher(XdgToplevelWindow window, bool skip)
    {
        ArgumentNullException.ThrowIfNull(window);
        UpdateFlags(window, ToplevelState.SkipSwitcher, skip ? ToplevelState.SkipSwitcher : ToplevelState.None);
    }

    public void SetMinimized(XdgToplevelWindow window, bool minimized)
    {
        ArgumentNullException.ThrowIfNull(window);
        UpdateFlags(window, ToplevelState.Minimized, minimized ? ToplevelState.Minimized : ToplevelState.None);
    }

    public void SetExcludedFromCapture(XdgToplevelWindow window, bool excluded)
    {
        ArgumentNullException.ThrowIfNull(window);
        UpdateFlags(
            window,
            ToplevelState.ExcludedFromCapture,
            excluded ? ToplevelState.ExcludedFromCapture : ToplevelState.None);
    }

    private void UpdateFlags(XdgToplevelWindow window, ToplevelState mask, ToplevelState value)
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
            case ToplevelRequestKind.Minimize or ToplevelRequestKind.Unminimize
                when MinimizeRequested is { } minimize:
                minimize.Invoke(window, request.Kind == ToplevelRequestKind.Minimize);
                return true;
            case ToplevelRequestKind.SetNoBorder or ToplevelRequestKind.UnsetNoBorder
                when NoBorderRequested is { } noBorder:
                noBorder.Invoke(window, request.Kind == ToplevelRequestKind.SetNoBorder);
                return true;
            case ToplevelRequestKind.ExcludeFromCapture or ToplevelRequestKind.IncludeInCapture
                when CaptureExclusionRequested is { } exclusion:
                exclusion.Invoke(window, request.Kind == ToplevelRequestKind.ExcludeFromCapture);
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
        _pids[id] = window.Surface.Resource.Client.TryGetCredentials(out var credentials)
            ? (uint)credentials.Pid
            : 0;
        window.TitleChanged += () => _observers.Changed(id);
        window.AppIdChanged += () => _observers.Changed(id);
        window.ParentChanged += () => _observers.Changed(id);
        window.Destroyed += () =>
        {
            _windows.Remove(id);
            _ids.Remove(window);
            _geometry.Remove(id);
            _flags.Remove(id);
            _pids.Remove(id);
            _appMenus.Remove(id);
            _observers.Removed(id);
        };
        _observers.Added(id);
    }

    private ToplevelInfo Describe(ulong id, XdgToplevelWindow window)
    {
        var state = _flags.GetValueOrDefault(id);
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

        var (frame, client) = _geometry.GetValueOrDefault(id);
        var (service, objectPath) = _appMenus.GetValueOrDefault(id, (string.Empty, string.Empty));
        return new ToplevelInfo(
            id,
            window.Title,
            window.AppId,
            state,
            window.Surface,
            frame,
            client,
            Pid: _pids.GetValueOrDefault(id),
            ParentId: window.Parent is { } parent ? _ids.GetValueOrDefault(parent) : 0,
            AppMenuService: service,
            AppMenuObjectPath: objectPath);
    }
}
