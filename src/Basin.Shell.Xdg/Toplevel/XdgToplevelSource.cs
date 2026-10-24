using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <see cref="IToplevelSource"/> for the xdg-shell toplevels of one <see cref="XdgShell"/>. Ids start at 1 and are not reused. Geometry, flags and requests are fed by the consumer's policy.
/// </summary>
public sealed class XdgToplevelSource : IToplevelSource, IToplevelGeometryFeed, IDisposable
{
    private readonly XdgShell _shell;
    private readonly Dictionary<ulong, XdgToplevelWindow> _windows = [];
    private readonly Dictionary<XdgToplevelWindow, ulong> _ids = [];
    private readonly Dictionary<ulong, (Box Frame, Box Client)> _geometry = [];
    private readonly Dictionary<ulong, ToplevelState> _flags = [];
    private readonly Dictionary<ulong, uint> _pids = [];
    private readonly Dictionary<ulong, (string Service, string ObjectPath)> _appMenus = [];
    private readonly Dictionary<ulong, Box> _minimized = [];
    private ulong _nextId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgToplevelSource"/> class. It tracks only the toplevels that the shell makes after this call.
    /// </summary>
    /// <param name="shell">The shell to watch.</param>
    public XdgToplevelSource(XdgShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        _shell.NewToplevel += Track;
    }

    private readonly ToplevelObservers _observers = new();
    private readonly ToplevelCommitObservers _commitObservers = new();
    private readonly Dictionary<ulong, SurfaceTreeCommitWatch> _commitWatches = [];

    /// <summary>
    /// Gets a value indicating whether the source sends content commits to commit observers. It is always <see langword="true"/>.
    /// </summary>
    public bool ReportsCommits => true;

    /// <summary>
    /// Finds whether a mapped toplevel did not acknowledge its newest configure, or acknowledged a new size and did not commit it yet.
    /// A configure that keeps the size is answered by its acknowledgement, because xdg-shell does not require a commit after it.
    /// </summary>
    /// <param name="localId">The local id of the toplevel.</param>
    /// <returns><see langword="true"/> when the toplevel exists, is mapped and still owes the answer.</returns>
    public bool IsConfigurePending(ulong localId)
    {
        if (!_windows.TryGetValue(localId, out var window) || window.Xdg is not { IsMapped: true } xdg)
        {
            return false;
        }

        if (xdg.HasUnackedConfigure)
        {
            return true;
        }

        if (!xdg.IsConfigurePending)
        {
            return false;
        }

        var (width, height) = window.ConfiguredSize;
        var geometry = xdg.PresentedGeometry;
        return (width > 0 && width != geometry.Width) || (height > 0 && height != geometry.Height);
    }

    /// <inheritdoc/>
    public void AddCommitObserver(IToplevelCommitObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _commitObservers.Add(observer);
        if (_commitObservers.Count == 1)
        {
            foreach (var (id, window) in _windows)
            {
                WatchCommits(id, window);
            }
        }
    }

    /// <inheritdoc/>
    public void RemoveCommitObserver(IToplevelCommitObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _commitObservers.Remove(observer);
        if (_commitObservers.Count > 0)
        {
            return;
        }

        foreach (var watch in _commitWatches.Values)
        {
            watch.Dispose();
        }

        _commitWatches.Clear();
    }

    private void WatchCommits(ulong id, XdgToplevelWindow window)
    {
        if (!_commitWatches.ContainsKey(id))
        {
            _commitWatches[id] = new SurfaceTreeCommitWatch(window.Surface, damage => _commitObservers.Committed(id, damage));
        }
    }

    /// <inheritdoc/>
    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Occurs for an activate request that <see cref="RequestHandler"/> did not answer. Without a handler, the request is declined.
    /// </summary>
    public event Action<XdgToplevelWindow>? ActivateRequested;

    /// <summary>
    /// Occurs for a request to set or unset the no-border state. The argument is <see langword="true"/> for no border. Without a handler, the request is declined.
    /// </summary>
    public event Action<XdgToplevelWindow, bool>? NoBorderRequested;

    /// <summary>
    /// Occurs for a request to minimize or unminimize. The argument is <see langword="true"/> to minimize. Without a handler, the request is declined.
    /// </summary>
    public event Action<XdgToplevelWindow, bool>? MinimizeRequested;

    /// <summary>
    /// Occurs for a request to exclude the window from capture or to include it. The argument is <see langword="true"/> to exclude. Without a handler, the request is declined.
    /// </summary>
    public event Action<XdgToplevelWindow, bool>? CaptureExclusionRequested;

    /// <summary>
    /// Occurs for a request to set or unset the minimized geometry. The arguments are the window, the surface that the box is relative to, and the box, which is empty for unset.
    /// </summary>
    public event Action<XdgToplevelWindow, Surface?, Box>? MinimizedGeometryRequested;

    /// <summary>
    /// Gets or sets the policy that answers move and resize requests. Without one, the source declines them.
    /// </summary>
    public IToplevelPlacement? Placement { get; set; }

    /// <summary>
    /// Gets or sets the handler that sees each request first. A non-null answer from it skips the default handling and the request events.
    /// </summary>
    public XdgToplevelRequestHandler? RequestHandler { get; set; }

    /// <summary>
    /// Finds the window of a local id.
    /// </summary>
    /// <param name="localId">The local id.</param>
    /// <returns>The window, or <see langword="null"/> when the id is unknown or the window is destroyed.</returns>
    public XdgToplevelWindow? WindowFor(ulong localId) => _windows.GetValueOrDefault(localId);

    /// <summary>
    /// Finds the local id of a window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The local id, or 0 when the source does not track the window.</returns>
    public ulong IdFor(XdgToplevelWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _ids.GetValueOrDefault(window);
    }

    /// <summary>
    /// Sets the frame and client boxes that the source reports for a window. A change notifies the observers. An untracked window is ignored.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="frame">The box of the window with its frame, in layout coordinates.</param>
    /// <param name="client">The box of the client's window geometry, in layout coordinates.</param>
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

    /// <summary>
    /// Sets the geometry of each tracked window whose root surface is in <paramref name="boxes"/>. The frame and the client box are both the window-geometry box in layout coordinates.
    /// </summary>
    /// <param name="boxes">The layout box of each surface that the scene drew. The span is valid only during the call.</param>
    public void ReportFrom(ReadOnlySpan<SurfaceBox> boxes)
    {
        foreach (var (_, window) in _windows)
        {
            var surface = window.Surface;
            for (var i = 0; i < boxes.Length; i++)
            {
                if (!ReferenceEquals(boxes[i].Surface, surface))
                {
                    continue;
                }

                var drawn = boxes[i].Box;
                var geometry = window.Xdg.PresentedGeometry;
                var client = new Box(drawn.X + geometry.X, drawn.Y + geometry.Y, geometry.Width, geometry.Height);
                SetGeometry(window, client, client);
                break;
            }
        }
    }

    /// <summary>
    /// Sets the minimized geometry that the source reports for a window. A change notifies the observers. An untracked window is ignored.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="geometry">The box that the window minimizes to, or an empty box to clear it.</param>
    public void SetMinimizedGeometry(XdgToplevelWindow window, in Box geometry)
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

    /// <summary>
    /// Sets the no-border flags that the source reports for a window. A change notifies the observers.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="noBorder"><see langword="true"/> when the window has no server-side frame.</param>
    /// <param name="userCanSet"><see langword="true"/> when the user can change the no-border state.</param>
    public void SetDecoration(XdgToplevelWindow window, bool noBorder, bool userCanSet)
    {
        ArgumentNullException.ThrowIfNull(window);
        var flags = (noBorder ? ToplevelState.NoBorder : ToplevelState.None) |
            (userCanSet ? ToplevelState.CanSetNoBorder : ToplevelState.None);
        UpdateFlags(window, ToplevelState.NoBorder | ToplevelState.CanSetNoBorder, flags);
    }

    /// <summary>
    /// Sets the D-Bus address of the window's application menu. A change notifies the observers. An untracked window is ignored.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="serviceName">The D-Bus service name.</param>
    /// <param name="objectPath">The D-Bus object path.</param>
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

    /// <summary>
    /// Sets whether a taskbar leaves the window out. A change notifies the observers.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="skip"><see langword="true"/> to leave the window out.</param>
    public void SetSkipTaskbar(XdgToplevelWindow window, bool skip)
    {
        ArgumentNullException.ThrowIfNull(window);
        UpdateFlags(window, ToplevelState.SkipTaskbar, skip ? ToplevelState.SkipTaskbar : ToplevelState.None);
    }

    /// <summary>
    /// Sets whether a window switcher leaves the window out. A change notifies the observers.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="skip"><see langword="true"/> to leave the window out.</param>
    public void SetSkipSwitcher(XdgToplevelWindow window, bool skip)
    {
        ArgumentNullException.ThrowIfNull(window);
        UpdateFlags(window, ToplevelState.SkipSwitcher, skip ? ToplevelState.SkipSwitcher : ToplevelState.None);
    }

    /// <summary>
    /// Sets the minimized flag that the source reports for a window. A change notifies the observers. The client is not told.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="minimized"><see langword="true"/> when the window is minimized.</param>
    public void SetMinimized(XdgToplevelWindow window, bool minimized)
    {
        ArgumentNullException.ThrowIfNull(window);
        UpdateFlags(window, ToplevelState.Minimized, minimized ? ToplevelState.Minimized : ToplevelState.None);
    }

    /// <summary>
    /// Sets the flag that excludes a window from screen capture. A change notifies the observers.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="excluded"><see langword="true"/> to exclude the window.</param>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <summary>
    /// Asks the source to act on one of its toplevels. <see cref="RequestHandler"/> answers first. Otherwise close, maximize and fullscreen have a default, and the other kinds go to their events.
    /// </summary>
    /// <param name="localId">The local id of the toplevel.</param>
    /// <param name="request">The action and its arguments.</param>
    /// <returns><see langword="true"/> when the request was accepted. <see langword="false"/> when the id is unknown, the handler declined, or no event handler is attached for the kind.</returns>
    public bool Request(ulong localId, in ToplevelRequest request)
    {
        if (!_windows.TryGetValue(localId, out var window))
        {
            return false;
        }

        if (RequestHandler?.Invoke(window, request) is { } answered)
        {
            return answered;
        }

        switch (request.Kind)
        {
            case ToplevelRequestKind.Activate when ActivateRequested is { } activate:
                activate.Invoke(window);
                return true;
            case ToplevelRequestKind.Close:
                window.Close();
                return true;
            case ToplevelRequestKind.Maximize or ToplevelRequestKind.Unmaximize:
                var maximized = request.Kind == ToplevelRequestKind.Maximize;
                if (!window.RaiseMaximizeRequest(maximized))
                {
                    window.SetMaximized(maximized);
                }

                window.RequestConfigure();
                return true;
            case ToplevelRequestKind.Fullscreen or ToplevelRequestKind.Unfullscreen:
                var fullscreen = request.Kind == ToplevelRequestKind.Fullscreen;
                if (!window.RaiseFullscreenRequest(fullscreen))
                {
                    window.SetFullscreen(fullscreen);
                }

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
            case ToplevelRequestKind.Move when Placement is { } placement:
                return placement.Move(window, request.Geometry.X, request.Geometry.Y);
            case ToplevelRequestKind.Resize when Placement is { } placement:
                return placement.Resize(window, request.Geometry.Width, request.Geometry.Height);
            default:
                return false;
        }
    }

    /// <summary>
    /// Stops tracking new toplevels and stops the commit watches.
    /// </summary>
    public void Dispose()
    {
        _shell.NewToplevel -= Track;
        foreach (var watch in _commitWatches.Values)
        {
            watch.Dispose();
        }

        _commitWatches.Clear();
    }

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
        window.StateChanged += () => _observers.Changed(id);
        if (_commitObservers.Count > 0)
        {
            WatchCommits(id, window);
        }

        window.Destroyed += () =>
        {
            if (_commitWatches.Remove(id, out var watch))
            {
                watch.Dispose();
            }

            _windows.Remove(id);
            _ids.Remove(window);
            _geometry.Remove(id);
            _flags.Remove(id);
            _pids.Remove(id);
            _appMenus.Remove(id);
            _minimized.Remove(id);
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
            AppMenuObjectPath: objectPath,
            MinimizedGeometry: _minimized.GetValueOrDefault(id));
    }
}
