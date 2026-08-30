using Basin.Config;
using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

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

    public uint WlSeatName { get; private set; }

    public Point PointerPosition { get; private set; }

    public WmWindow? PointerFocus { get; private set; }

    public bool IsRemoved { get; private set; }

    public PointerOperation? Operation => _operation;

    public event Action<WmWindow>? WindowInteraction;

    public event Action<WmShellSurface>? ShellSurfaceInteraction;

    public event Action? Removed;

    public void FocusWindow(WmWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _wm.EnsureManage(nameof(FocusWindow));
        _proxy.FocusWindow(window.Proxy);
    }

    public void FocusShellSurface(WmShellSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _wm.EnsureManage(nameof(FocusShellSurface));
        _proxy.FocusShellSurface(surface.Proxy);
    }

    public void ClearFocus()
    {
        _wm.EnsureManage(nameof(ClearFocus));
        _proxy.ClearFocus();
    }

    public void WarpPointer(int x, int y)
    {
        _wm.EnsureManage(nameof(WarpPointer));
        _wm.RequireVersion(3, "pointer_warp");
        _proxy.PointerWarp(x, y);
    }

    public void WarpPointer(Point position) => WarpPointer(position.X, position.Y);

    public void SetCursorTheme(string name, int size)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        WmThreadAffinity.Assert();
        _wm.RequireVersion(2, "set_xcursor_theme");
        _proxy.SetXcursorTheme(name, (uint)size);
    }

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
