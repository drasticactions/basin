using Basin.Capabilities;

namespace Basin.XWayland;

/// <summary>
/// Publishes the managed windows of one <see cref="XWaylandWm"/> as an <see cref="IToplevelSource"/>. Override-redirect windows are not toplevels. Local ids start at 1 and are never reused.
/// </summary>
public sealed class XWaylandToplevelSource : IToplevelSource, IToplevelGeometryFeed, IDisposable
{
    private readonly XWaylandWm _wm;
    private readonly Dictionary<ulong, XWaylandWindow> _windows = [];
    private readonly Dictionary<XWaylandWindow, ulong> _ids = [];
    private readonly Dictionary<ulong, (Box Frame, Box Client)> _geometry = [];
    private readonly Dictionary<ulong, ToplevelState> _flags = [];
    private readonly Dictionary<ulong, uint> _pids = [];
    private readonly Dictionary<ulong, Box> _minimized = [];
    private ulong _nextId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XWaylandToplevelSource"/> class with no display, so the snapshots carry no X11 display or density.
    /// </summary>
    /// <param name="wm">The window manager whose mapped windows the source tracks.</param>
    public XWaylandToplevelSource(XWaylandWm wm)
        : this(wm, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XWaylandToplevelSource"/> class. Windows that mapped before this call are not tracked.
    /// </summary>
    /// <param name="wm">The window manager whose mapped windows the source tracks.</param>
    /// <param name="display">The display that names the X11 display and density in each snapshot, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
    public XWaylandToplevelSource(XWaylandWm wm, XWaylandDisplay? display)
    {
        ArgumentNullException.ThrowIfNull(wm);
        _wm = wm;
        Display = display;
        _wm.WindowMapped += Track;
        _wm.KeyboardFocusChanged += OnKeyboardFocusChanged;
    }

    private void OnKeyboardFocusChanged(Surface? previous, Surface? focus)
    {
        foreach (var (window, id) in _ids)
        {
            if (window.Surface is { } surface && (surface == previous || surface == focus))
            {
                _observers.Changed(id);
            }
        }
    }

    private readonly ToplevelObservers _observers = new();

    /// <summary>
    /// Gets the display of the window manager, or <see langword="null"/> when the source was created without one.
    /// </summary>
    public XWaylandDisplay? Display { get; }

    /// <inheritdoc/>
    public void AddObserver(IToplevelObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IToplevelObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Occurs on a set or unset no-border request. The argument is <see langword="true"/> to remove the border. Without a handler, <see cref="Request"/> declines the request.
    /// </summary>
    public event Action<XWaylandWindow, bool>? NoBorderRequested;

    /// <summary>
    /// Occurs on a capture exclusion request. The argument is <see langword="true"/> to exclude the window. Without a handler, <see cref="Request"/> declines the request.
    /// </summary>
    public event Action<XWaylandWindow, bool>? CaptureExclusionRequested;

    /// <summary>
    /// Occurs on a set or unset minimized-geometry request. The arguments are the window, the surface that the box is relative to, and the box, which is empty for an unset.
    /// </summary>
    public event Action<XWaylandWindow, Surface?, Box>? MinimizedGeometryRequested;

    /// <summary>
    /// Gets or sets the policy that answers move and resize requests. Without one, <see cref="Request"/> declines them.
    /// </summary>
    public IToplevelPlacement? Placement { get; set; }

    /// <summary>
    /// Finds the window of a local id.
    /// </summary>
    /// <param name="localId">The local id.</param>
    /// <returns>The window, or <see langword="null"/> when the id is unknown or the window is destroyed.</returns>
    public XWaylandWindow? WindowFor(ulong localId) => _windows.GetValueOrDefault(localId);

    /// <summary>
    /// Finds the local id of a window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The local id, or 0 when the source does not track the window.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public ulong IdFor(XWaylandWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _ids.GetValueOrDefault(window);
    }

    /// <inheritdoc/>
    public void ReportFrom(ReadOnlySpan<SurfaceBox> boxes)
    {
        foreach (var (_, window) in _windows)
        {
            if (window.Surface is not { } surface)
            {
                continue;
            }

            for (var i = 0; i < boxes.Length; i++)
            {
                if (ReferenceEquals(boxes[i].Surface, surface))
                {
                    SetGeometry(window, boxes[i].Box, boxes[i].Box);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Sets the geometry that the snapshots report, for example when the compositor draws a frame around the window. Observers get a change only when a value changes.
    /// </summary>
    /// <param name="window">The window. An untracked window is ignored.</param>
    /// <param name="frame">The outer box with decorations, in layout coordinates.</param>
    /// <param name="client">The box of the client content, in layout coordinates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Sets the minimized geometry that the snapshots report. An empty box clears it.
    /// </summary>
    /// <param name="window">The window. An untracked window is ignored.</param>
    /// <param name="geometry">The box that a minimize animation targets, for example a taskbar button.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Sets the no-border flags that the snapshots report.
    /// </summary>
    /// <param name="window">The window. An untracked window is ignored.</param>
    /// <param name="noBorder"><see langword="true"/> when the compositor draws no border.</param>
    /// <param name="userCanSet"><see langword="true"/> when a client can change the border through a request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    public void SetDecoration(XWaylandWindow window, bool noBorder, bool userCanSet)
    {
        ArgumentNullException.ThrowIfNull(window);
        var flags = (noBorder ? ToplevelState.NoBorder : ToplevelState.None) |
            (userCanSet ? ToplevelState.CanSetNoBorder : ToplevelState.None);
        UpdateFlags(window, ToplevelState.NoBorder | ToplevelState.CanSetNoBorder, flags);
    }

    /// <summary>
    /// Sets the capture exclusion flag that the snapshots report.
    /// </summary>
    /// <param name="window">The window. An untracked window is ignored.</param>
    /// <param name="excluded"><see langword="true"/> when screen capture leaves the window out.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
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
    public bool IsConfigurePending(ulong localId) =>
        _windows.TryGetValue(localId, out var window) && window.IsConfigurePending;

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
    /// Acts on a window. Activate, close, minimize and unminimize are always accepted. Maximize and fullscreen go to the window's own request events, as if the client asked. The other kinds go to the matching event or to <see cref="Placement"/>, and are declined when nothing handles them.
    /// </summary>
    /// <param name="localId">The local id of the window.</param>
    /// <param name="request">The action and its arguments.</param>
    /// <returns><see langword="false"/> when the id is unknown or the request is declined.</returns>
    public bool Request(ulong localId, in ToplevelRequest request)
    {
        if (!_windows.TryGetValue(localId, out var window))
        {
            return false;
        }

        switch (request.Kind)
        {
            case ToplevelRequestKind.Activate:
                _wm.RequestActivation(window);
                return true;
            case ToplevelRequestKind.Close:
                window.Close();
                return true;
            case ToplevelRequestKind.Minimize or ToplevelRequestKind.Unminimize:
                window.RaiseMinimizeRequested(request.Kind == ToplevelRequestKind.Minimize);
                return true;
            case ToplevelRequestKind.Maximize or ToplevelRequestKind.Unmaximize:
                return window.RaiseMaximizeRequested(request.Kind == ToplevelRequestKind.Maximize);
            case ToplevelRequestKind.Fullscreen or ToplevelRequestKind.Unfullscreen:
                return window.RaiseFullscreenRequested(request.Kind == ToplevelRequestKind.Fullscreen);
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
    /// Stops tracking newly mapped windows and focus changes. Windows that the source already tracks stay.
    /// </summary>
    public void Dispose()
    {
        _wm.WindowMapped -= Track;
        _wm.KeyboardFocusChanged -= OnKeyboardFocusChanged;
    }

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
        window.AppMenuChanged += () => _observers.Changed(id);
        window.GeometryChanged += () => _observers.Changed(id);
        window.PropertiesChanged += () => _observers.Changed(id);
        window.StateChanged += () => _observers.Changed(id);
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

        if (window.IsMaximized)
        {
            state |= ToplevelState.Maximized;
        }

        if (window.IsFullscreen)
        {
            state |= ToplevelState.Fullscreen;
        }

        if (window.Surface is { } focused && focused == _wm.KeyboardFocus)
        {
            state |= ToplevelState.Activated;
        }

        var pid = window.PidVerified ? (uint)window.Pid : 0;
        if (pid == 0 && !_pids.TryGetValue(id, out pid) && window.Surface is { } surface)
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
            AppMenuService: window.AppMenuServiceName,
            AppMenuObjectPath: window.AppMenuObjectPath,
            MinimizedGeometry: _minimized.GetValueOrDefault(id),
            X11Display: Display?.DisplayName,
            X11Density: Display?.Density);
    }
}
