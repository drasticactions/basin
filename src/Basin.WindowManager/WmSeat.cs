using Basin.Config;
using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// One user's input devices as the compositor reports them to the manager: keyboard focus, pointer position and interactions with windows.
/// </summary>
public sealed class WmSeat
{
    private readonly RiverWindowManager _wm;
    private readonly RiverSeatV1 _proxy;
    private readonly List<WmWindow> _windowInteractions = [];
    private readonly List<WmShellSurface> _shellSurfaceInteractions = [];
    private readonly List<RiverWindowV1> _pendingWindowInteractions = [];
    private readonly List<RiverShellSurfaceV1> _pendingShellSurfaceInteractions = [];

    private Point _pendingPointerPosition;
    private bool _pointerPositionChanged;
    private RiverWindowV1? _pendingPointerFocus;
    private bool _pointerFocusChanged;
    private bool _removedPending;
    private PointerOperation? _operation;

    internal WmSeat(RiverWindowManager wm, RiverSeatV1 proxy)
    {
        _wm = wm;
        _proxy = proxy;

        proxy.WlSeat += (_, e) => WlSeatName = e.Name;
        proxy.PointerPosition += (_, e) =>
        {
            _pendingPointerPosition = new Point(e.X, e.Y);
            _pointerPositionChanged = true;
        };
        proxy.PointerEnter += (_, e) =>
        {
            _pendingPointerFocus = e.Window;
            _pointerFocusChanged = true;
        };
        proxy.PointerLeave += (_, _) =>
        {
            _pendingPointerFocus = null;
            _pointerFocusChanged = true;
        };
        proxy.WindowInteraction += (_, e) =>
        {
            if (e.Window is not null)
            {
                _pendingWindowInteractions.Add(e.Window);
            }
        };
        proxy.ShellSurfaceInteraction += (_, e) =>
        {
            if (e.ShellSurface is not null)
            {
                _pendingShellSurfaceInteractions.Add(e.ShellSurface);
            }
        };
        proxy.OpDelta += (_, e) => _operation?.ReportDelta(new Point(e.Dx, e.Dy));
        proxy.OpRelease += (_, _) => _operation?.ReportReleased();
        proxy.Removed += (_, _) =>
        {
            IsRemoved = true;
            _removedPending = true;
            _wm.OnSeatRemoved(this);
        };
    }

    /// <summary>
    /// Gets the registry name of the matching <c>wl_seat</c> global.
    /// </summary>
    public uint WlSeatName { get; private set; }

    /// <summary>
    /// Gets the pointer position in the compositor's logical space, as of the start of the current sequence. A move alone starts no sequence.
    /// </summary>
    public Point PointerPosition { get; private set; }

    /// <summary>
    /// Gets the window under the pointer, as of the start of the current sequence, or <see langword="null"/> when the pointer is over no window.
    /// </summary>
    public WmWindow? PointerFocus { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the compositor removed the seat. It becomes true when the event arrives, before <see cref="Removed"/> fires.
    /// </summary>
    public bool IsRemoved { get; private set; }

    /// <summary>
    /// Gets the interactive pointer operation in progress, or <see langword="null"/> when none is in progress.
    /// </summary>
    public PointerOperation? Operation => _operation;

    /// <summary>
    /// Occurs at the start of a sequence for each window that the user clicked, touched or used a tablet tool on.
    /// </summary>
    public event Action<WmWindow>? WindowInteraction;

    /// <summary>
    /// Occurs at the start of a sequence for each manager shell surface that the user clicked, touched or used a tablet tool on.
    /// </summary>
    public event Action<WmShellSurface>? ShellSurfaceInteraction;

    /// <summary>
    /// Occurs once at the start of the next sequence after the compositor removes the seat. The library destroys the seat when that manage sequence ends.
    /// </summary>
    public event Action? Removed;

    /// <summary>
    /// Sends the seat's keyboard input to a window.
    /// </summary>
    /// <param name="window">The window to focus.</param>
    /// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void FocusWindow(WmWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _wm.EnsureManage(nameof(FocusWindow));
        _proxy.FocusWindow(window.Proxy);
    }

    /// <summary>
    /// Sends the seat's keyboard input to one of the manager's shell surfaces.
    /// </summary>
    /// <param name="surface">The shell surface to focus.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void FocusShellSurface(WmShellSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _wm.EnsureManage(nameof(FocusShellSurface));
        _proxy.FocusShellSurface(surface.Proxy);
    }

    /// <summary>
    /// Removes the seat's keyboard focus, so no surface gets its keys.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void ClearFocus()
    {
        _wm.EnsureManage(nameof(ClearFocus));
        _proxy.ClearFocus();
    }

    /// <summary>
    /// Moves the pointer. The compositor moves a position outside every output to the nearest point inside one.
    /// </summary>
    /// <param name="x">The horizontal coordinate in the compositor's logical space.</param>
    /// <param name="y">The vertical coordinate in the compositor's logical space.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">The bound protocol version is lower than 3.</exception>
    public void WarpPointer(int x, int y)
    {
        _wm.EnsureManage(nameof(WarpPointer));
        _wm.RequireVersion(3, "pointer_warp");
        _proxy.PointerWarp(x, y);
    }

    /// <summary>
    /// Moves the pointer to a point.
    /// </summary>
    /// <param name="position">The position in the compositor's logical space.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">The bound protocol version is lower than 3.</exception>
    public void WarpPointer(Point position) => WarpPointer(position.X, position.Y);

    /// <summary>
    /// Sets the XCursor theme for cursors that the compositor draws on this seat. It needs no sequence.
    /// </summary>
    /// <param name="name">The theme name.</param>
    /// <param name="size">The cursor size. It must be positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is zero or negative.</exception>
    /// <exception cref="NotSupportedException">The bound protocol version is lower than 2.</exception>
    public void SetCursorTheme(string name, int size)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        WmThreadAffinity.Assert();
        _wm.RequireVersion(2, "set_xcursor_theme");
        _proxy.SetXcursorTheme(name, (uint)size);
    }

    /// <summary>
    /// Starts an interactive pointer operation for a move or a resize. When one is in progress, it returns that one, because the protocol ignores a second start.
    /// </summary>
    /// <returns>The operation in progress on this seat.</returns>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public PointerOperation StartPointerOperation()
    {
        _wm.EnsureManage(nameof(StartPointerOperation));
        if (_operation is { IsEnded: false })
        {
            return _operation;
        }

        _proxy.OpStartPointer();
        _operation = new PointerOperation(_wm, this);
        return _operation;
    }

    /// <summary>
    /// Creates a pointer binding on this seat. It stays inert until <see cref="PointerBinding.Enable"/> runs in a manage sequence.
    /// </summary>
    /// <param name="button">A Linux input event code, for example <c>BTN_LEFT</c>.</param>
    /// <param name="modifiers">The modifiers that must be held.</param>
    /// <param name="pressed">An optional handler for <see cref="PointerBinding.Pressed"/>.</param>
    /// <returns>The new binding.</returns>
    public PointerBinding BindPointer(uint button, Modifiers modifiers, Action? pressed = null)
    {
        WmThreadAffinity.Assert();
        var binding = new PointerBinding(
            _wm,
            _proxy.GetPointerBinding(button, (RiverSeatV1.Modifiers)modifiers));
        if (pressed is not null)
        {
            binding.Pressed += pressed;
        }

        return binding;
    }

    /// <inheritdoc/>
    public override string ToString() => $"seat {WlSeatName}";

    internal RiverSeatV1 Proxy => _proxy;

    internal void EndOperation(PointerOperation operation)
    {
        if (ReferenceEquals(_operation, operation))
        {
            _proxy.OpEnd();
            _operation = null;
        }
    }

    internal void ApplyPending()
    {
        if (_pointerPositionChanged)
        {
            (PointerPosition, _pointerPositionChanged) = (_pendingPointerPosition, false);
        }

        if (_pointerFocusChanged)
        {
            PointerFocus = _wm.Resolve(_pendingPointerFocus);
            _pointerFocusChanged = false;
        }
    }

    internal void FirePending()
    {
        if (_pendingWindowInteractions.Count > 0)
        {
            _windowInteractions.Clear();
            foreach (var proxy in _pendingWindowInteractions)
            {
                if (_wm.Resolve(proxy) is { } window)
                {
                    _windowInteractions.Add(window);
                }
            }

            _pendingWindowInteractions.Clear();
            foreach (var window in _windowInteractions)
            {
                WindowInteraction?.Invoke(window);
            }
        }

        if (_pendingShellSurfaceInteractions.Count > 0)
        {
            _shellSurfaceInteractions.Clear();
            foreach (var proxy in _pendingShellSurfaceInteractions)
            {
                if (_wm.Resolve(proxy) is { } surface)
                {
                    _shellSurfaceInteractions.Add(surface);
                }
            }

            _pendingShellSurfaceInteractions.Clear();
            foreach (var surface in _shellSurfaceInteractions)
            {
                ShellSurfaceInteraction?.Invoke(surface);
            }
        }

        _operation?.FirePending();

        if (_removedPending)
        {
            _removedPending = false;
            Removed?.Invoke();
        }
    }

    internal void DestroyProxy()
    {
        if (!_proxy.IsDestroyed)
        {
            _proxy.Destroy();
        }
    }
}
