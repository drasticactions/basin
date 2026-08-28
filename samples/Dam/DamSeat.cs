using Basin;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Basin.Host;
using Basin.Scene;
using Basin.Seat;
using Basin.Seat.Backends;
using Xkb;

using Basin.Diagnostics;

namespace Dam;

internal sealed class DamSeat :
    IDisposable,
    ITouchPointerTarget,
    ITouchActivitySink
{
    private static readonly XkbKeysym Escape = XkbKeysym.FromName("Escape");
    private static readonly XkbKeysym SwitchVt1 = XkbKeysym.FromName("XF86Switch_VT_1");
    private static readonly XkbKeysym SwitchVt12 = XkbKeysym.FromName("XF86Switch_VT_12");

    private readonly Basin.Host.BasinHost _host;
    private readonly Basin.Seat.Seat _seat;
    private readonly DamViews _views;
    private readonly OutputDriver _outputs;
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private readonly LayoutPointer _pointer;
    private readonly CursorController _cursor;
    private readonly CursorImageTheme _cursorTheme;
    private readonly SeatIdleSource _idle;
    private readonly PointerDelivery _delivery;
    private readonly Action _stop;
    private readonly bool _allowVtSwitch;
    private readonly BasinLogger _log;

    private readonly SeatBinder _binder;
    private readonly SeatInjector _injector;

    private readonly SeatTouchDriver _touch;

    private LibinputBackend? _libinput;
    private readonly PointerRefresh _pointerRefresh;
    private int _touchId = -1;
    private readonly DragIconFollower _dragIcon;

    public DamSeat(
        Basin.Host.BasinHost host,
        BasinServices services,
        DamViews views,
        OutputDriver outputs,
        Scene scene,
        OutputLayout layout,
        CursorImageTheme cursorTheme,
        Basin.Seat.Backends.HookInputSink inputSink,
        SeatIdleSource idle,
        bool allowVtSwitch,
        Action stop,
        BasinLogger log)
    {
        _host = host;
        _seat = services.Require<Basin.Seat.Seat>();
        _views = views;
        _outputs = outputs;
        _scene = scene;
        _layout = layout;
        _cursorTheme = cursorTheme;
        _idle = idle;
        _allowVtSwitch = allowVtSwitch;
        _stop = stop;
        _log = log;

        _pointer = new LayoutPointer(layout);
        _cursor = new CursorController(layout)
        {
            Shapes = services.Require<CursorShapeManager>(),
            Capture = services.Find<Basin.Capabilities.IScreenCapture>(),
        };
        _cursor.Shapes.CursorRequested += _cursor.ShowImage;
        _delivery = new PointerDelivery(_seat, _cursor) { RelativePointer = services.Require<RelativePointerManager>() };
        _binder = new SeatBinder(_seat, layout, _pointer, _cursor)
        {
            Drm = host.Drm,
            Theme = cursorTheme,
        };
        _binder.Key += OnKey;
        _binder.ModifiersChanged += _idle.NotifyActivity;
        _binder.Motion += ProcessCursorMotion;
        _binder.Button += OnButton;
        _binder.Axis += OnAxis;
        _binder.PointerLeft += _seat.Pointer.NotifyClearFocus;
        _dragIcon = new DragIconFollower(_seat, () => _scene.Root, () => (_pointer.X, _pointer.Y));
        _touch = new SeatTouchDriver(_binder, _seat);
        _dragIcon.Touch = _touch.Router;
        _touch.Router.HitTester = new SceneTouchHitTester(_scene);
        _touch.Router.Activity = this;
        _touch.AttachPointer(this);
        _touch.Routed += (id, kind, surface) =>
        {
            if (kind == TouchTargetKind.Client && _touchId < 0)
            {
                _touchId = id;
                _dragIcon.SetTouchSlot(id);
                if (_touch.Router.TryGetPosition(id, out var x, out var y))
                {
                    PressCursorButton(true, x, y);
                }
            }
        };

        _seat.Capabilities = SeatCapability.None;
        _seat.Keyboard.SetKeymap(SystemKeymap.Read());
        _seat.Keyboard.SetRepeatInfo(25, 600);
        _seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;

        _pointerRefresh = new PointerRefresh(
            _scene, _host.Loop, () => ProcessCursorMotion((uint)Environment.TickCount));

        _outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene);
        _outputs.Removed += view => _cursor.RemoveOutput(view.Output);

        inputSink.OnKey = (keyboard, timeMs, key, pressed) =>
        {
            _seat.Keyboard.Activate(keyboard);
            OnKey(timeMs, key, pressed);
            return true;
        };
        inputSink.OnKeyboardCreated = () => _seat.SetCapability(SeatCapability.Keyboard, true);
        inputSink.OnPointerMotion = (timeMs, dx, dy) =>
        {
            _binder.EnsurePointerCapability();
            _pointer.Motion(dx, dy);
            ProcessCursorMotion(timeMs, dx, dy);
            return true;
        };
        _injector = new SeatInjector(_binder, _seat, _layout, _pointer)
        {
            Moved = timeMs => ProcessCursorMotion(timeMs),
            MovedBy = (timeMs, dx, dy) => ProcessCursorMotion(timeMs, dx, dy),
            DeliverButton = OnButton,
            DeliverKey = OnKey,
        };
        inputSink.OnPointerMotionAbsolute = _injector.MotionAbsolute;
        inputSink.OnPointerButton = (timeMs, button, pressed) =>
        {
            OnButton(timeMs, button, pressed);
            return true;
        };

        if (_host.Drm is not null)
        {
            WireLibinput();
        }
        else if (_host.Parent is { } parent)
        {
            _binder.BindParent(parent);
        }
    }

    internal TouchRouter TouchRouter => _touch.Router;

    internal void Warp(double x, double y) => _injector.Warp(x, y);

    public void CenterCursor() => _injector.Center();

    private void WireLibinput()
    {
        var libinput = new LibinputBackend(_host.Loop, _host.Session!);
        _libinput = libinput;
        _binder.BindLibinput(libinput);
    }

    internal void OnKey(uint timeMs, uint key, bool pressed)
    {
        _idle.NotifyActivity();
        if (pressed && _seat.Keyboard.State?.IsModActive("Mod1") == true &&
            HandleKeybinding(_seat.Keyboard.KeysymFor(key)))
        {
            return;
        }

        _seat.Keyboard.NotifyKey(timeMs, key, pressed);
    }

    private bool HandleKeybinding(XkbKeysym symbol)
    {
#if DEBUG
        if (symbol == Escape)
        {
            _stop();
            return true;
        }
#endif
        if (_allowVtSwitch && symbol.Value >= SwitchVt1.Value && symbol.Value <= SwitchVt12.Value)
        {
            try
            {
                _host.Session?.SwitchSession((int)(symbol.Value - SwitchVt1.Value + 1));
            }
            catch (NotSupportedException)
            {
                _log.Debug($"no seat manager to switch sessions");
            }

            return true;
        }

        return false;
    }

    private void ProcessCursorMotion(uint timeMs, double dx = 0, double dy = 0, double? unaccelDx = null, double? unaccelDy = null)
    {
        _cursor.MoveTo(_pointer.X, _pointer.Y);
        var hit = _scene.SurfaceAt(_pointer.X, _pointer.Y);
        _delivery.Motion(timeMs, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, _pointer.X, _pointer.Y);
        _delivery.Relative(timeMs, dx, dy, unaccelDx, unaccelDy);

        PositionDragIcon();
        _idle.NotifyActivity();
    }

    internal void OnButton(uint timeMs, uint button, bool pressed)
    {
        _seat.Pointer.NotifyButton(timeMs, button, pressed);
        _seat.Pointer.NotifyFrame();
        PressCursorButton(pressed, _pointer.X, _pointer.Y);
        _idle.NotifyActivity();
    }

    private void OnAxis(uint timeMs, PointerAxis axis)
    {
        _seat.Pointer.NotifyAxis(timeMs, axis);
        _seat.Pointer.NotifyFrame();
        _idle.NotifyActivity();
    }

    private void PressCursorButton(bool pressed, double x, double y)
    {
        if (!pressed)
        {
            return;
        }

        var hit = _scene.SurfaceAt(x, y);
        var view = _views.OwnerOf(hit?.Surface);
        var current = _views.FocusedView;
        if (view is null || ReferenceEquals(view, current))
        {
            return;
        }

        if (current is null || !current.IsTransientFor(view))
        {
            _views.Focus(view);
        }
    }

    void ITouchActivitySink.OnTouchActivity()
    {
        PositionDragIcon();
        _idle.NotifyActivity();
    }

    void ITouchPointerTarget.Warp(uint timeMs, double x, double y)
    {
        _pointer.Warp(x, y);
        ProcessCursorMotion(timeMs);
    }

    void ITouchPointerTarget.Button(uint timeMs, uint button, bool pressed) =>
        OnButton(timeMs, button, pressed);

    private void PositionDragIcon() => _dragIcon.Follow();

    public void Dispose()
    {
        _pointerRefresh.Dispose();
        _cursor.Dispose();
        _libinput?.Dispose();
    }
}
