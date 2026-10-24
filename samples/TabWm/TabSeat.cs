using Basin;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Seat;

namespace TabWm;

internal sealed class TabSeat : IDisposable
{
    private readonly Seat _seat;
    private readonly Scene _scene;
    private readonly CursorController _cursor;
    private readonly LayoutPointer _pointer;
    private readonly UISurfaceRouter _router;
    private readonly Basin.Seat.Backends.SeatBinder _binder;
    private readonly Basin.Seat.Backends.SeatInjector _injector;
    private readonly Basin.Seat.Backends.SeatTouchDriver _touch;
    private readonly PointerRefresh _pointerRefresh;
    private readonly Basin.Seat.Backends.ButtonLatch _latch;
    private readonly Basin.Seat.Backends.ChromeKeyDelivery _keys;
    private readonly Basin.Seat.Backends.TouchChromeChain _touchChrome = new();
    private IUISurface? _routeSurface;
    private ButtonRoute _route;
    private double _x;
    private double _y;
    private bool _disposed;

    public TabSeat(
        Basin.Host.BasinHost host,
        BasinServices services,
        OutputLayout layout,
        Scene scene,
        CursorController cursor,
        UISurfaceIndex index,
        Basin.Backend.Libinput.LibinputBackend? input,
        Basin.Seat.Backends.HookInputSink inputSink)
    {
        _seat = services.Require<Seat>();
        _scene = scene;
        _cursor = cursor;
        _router = new UISurfaceRouter(scene, index) { ImplicitGrab = true };
        _latch = new Basin.Seat.Backends.ButtonLatch(_seat);
        _keys = new Basin.Seat.Backends.ChromeKeyDelivery(_seat, _router);
        _pointer = new LayoutPointer(layout);
        _pointer.Moved += () => MoveCursor((uint)Environment.TickCount);
        _pointerRefresh = new PointerRefresh(scene, host.Loop, RefreshPointer);

        _binder = new Basin.Seat.Backends.SeatBinder(_seat, layout, _pointer, cursor);
        _injector = new Basin.Seat.Backends.SeatInjector(_binder, _seat, layout, _pointer)
        {
            DeliverButton = OnButton,
            DeliverKey = OnKey,
            DeliverAxis = (timeMs, axis, value, source) =>
                OnAxis(timeMs, new PointerAxis((Wayland.WlPointer.Axis)axis, value, Source: (Wayland.WlPointer.AxisSource)source)),
        };
        inputSink.Route(_injector, _seat);
        _touch = new Basin.Seat.Backends.SeatTouchDriver(_binder, _seat);
        _touch.Router.HitTester = new Basin.Seat.Backends.SceneTouchHitTester(scene);
        _touchChrome.Then = new Basin.Seat.Backends.UISurfaceTouchChrome(_router);
        _touch.Router.Chrome = _touchChrome;
        _touch.Router.Activity = services.Find<IIdleSource>() as SeatIdleSource;

        _binder.RelativePointer = services.Find<RelativePointerManager>();
        _binder.Constraints = services.Find<PointerConstraintDriver>();
        _binder.Key += OnKey;
        _binder.ModifiersChanged += OnModifiers;
        _binder.Button += OnButton;
        _binder.Axis += OnAxis;
        _binder.PointerLeft += _router.PointerLeave;
        if (host.Parent is { } parent)
        {
            _binder.BindParent(parent);
        }

        if (input is not null)
        {
            input.DeviceAdded += device => BasinReport.Line($"INPUT + {device.Name}");
            input.DeviceRemoved += device => BasinReport.Line($"INPUT - {device.Name}");
            _binder.BindLibinput(input);
        }
    }

    private enum ButtonRoute
    {
        Seat,
        Frame,
        Chrome,
    }

    public Func<uint, uint, bool, bool>? KeyHook { get; set; }

    public Func<uint, uint, bool, bool>? ButtonHook { get; set; }

    public Func<double, double, uint, bool, bool>? FrameButtonHook { get; set; }

    public Func<double, double, string?>? FrameCursorAt { get; set; }

    public Func<IUISurface, bool>? OwnsFrame { get; set; }

    public Func<bool>? GrabActive { get; set; }

    public Action<double, double>? PointerMoved { get; set; }

    public Basin.Inspector.InspectorInput? Inspector
    {
        get => _touchChrome.First as Basin.Inspector.InspectorInput;
        set => _touchChrome.First = value;
    }

    public UISurfaceRouter Router => _router;

    public Basin.Seat.Backends.SeatBinder Binder => _binder;

    public Basin.Seat.Backends.SeatInjector Injector => _injector;

    public IUISurface? Hovered => _router.Hovered;

    public double PointerX => _x;

    public double PointerY => _y;

    public void CenterPointer() => _injector.Center();

    public void Warp(double x, double y) => _pointer.Warp(x, y);

    public void RefreshPointer() => MoveCursor((uint)Environment.TickCount);

    public void Dispose()
    {
        _disposed = true;
        _pointerRefresh.Dispose();
    }

    private bool Grabbed => GrabActive?.Invoke() == true;

    private void MoveCursor(uint time)
    {
        if (_disposed)
        {
            return;
        }

        _x = _pointer.X;
        _y = _pointer.Y;
        _cursor.MoveTo(_x, _y);
        PointerMoved?.Invoke(_x, _y);
        if (Grabbed)
        {
            return;
        }

        if (Inspector?.Motion(time, _x, _y, out var inspectorCursor) == true)
        {
            _router.PointerLeave();
            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed(inspectorCursor ?? "default");
            return;
        }

        var route = _router.PointerMotion(time, _x, _y);
        if (route.Surface is not null)
        {
            if (route.Entered)
            {
                _seat.Pointer.NotifyClearFocus();
            }

            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed(FrameCursorAt?.Invoke(_x, _y) ?? route.Cursor ?? "default");
            return;
        }

        if (_scene.SurfaceAt(_x, _y) is not { } hit || hit.Surface is null)
        {
            _seat.Pointer.NotifyClearFocus();
            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed("default");
            return;
        }

        _cursor.SetHover(hit.Surface, overClient: true);
        _seat.Pointer.NotifyMotionAt(time, hit.Surface, hit.X, hit.Y, _x, _y);
    }

    private void OnButton(uint time, uint button, bool pressed)
    {
        if (Inspector?.Button(time, button, pressed) == true)
        {
            return;
        }

        if (pressed)
        {
            if (_latch.Press())
            {
                _route = ChooseRoute();
            }

            Dispatch(time, button, pressed: true);
            return;
        }

        Dispatch(time, button, pressed: false);
        if (_latch.Release())
        {
            _route = ButtonRoute.Seat;
            _routeSurface = null;
        }
    }

    private ButtonRoute ChooseRoute()
    {
        if (_router.Hovered is { } surface)
        {
            _routeSurface = surface;
            return OwnsFrame?.Invoke(surface) == true ? ButtonRoute.Frame : ButtonRoute.Chrome;
        }

        _routeSurface = null;
        return ButtonRoute.Seat;
    }

    private void Dispatch(uint time, uint button, bool pressed)
    {
        if (_route != ButtonRoute.Seat)
        {
            if (Grabbed)
            {
                ButtonHook?.Invoke(time, button, pressed);
                return;
            }

            if (_route == ButtonRoute.Frame && FrameButtonHook?.Invoke(_x, _y, button, pressed) == true)
            {
                return;
            }

            if (_routeSurface is not null)
            {
                _router.PointerButton(time, button, pressed);
            }

            return;
        }

        if (pressed)
        {
            if (ButtonHook?.Invoke(time, button, true) == true)
            {
                return;
            }

            _latch.SeatButton(time, button, true);
            return;
        }

        ButtonHook?.Invoke(time, button, false);
        _latch.SeatButton(time, button, false);
    }

    public void ReleaseChrome(uint time, uint button)
    {
        if (_router.PointerGrab is not null)
        {
            _router.PointerButton(time, button, false);
        }
    }

    private void OnAxis(uint time, PointerAxis axis)
    {
        if (Inspector?.Axis(time, axis) == true || _router.PointerAxis(time, axis))
        {
            return;
        }

        _seat.Pointer.NotifyAxis(time, axis);
    }

    private void OnKey(uint time, uint key, bool pressed)
    {
        if (KeyHook?.Invoke(time, key, pressed) == true)
        {
            return;
        }

        _keys.Deliver(time, key, pressed);
    }

    private void OnModifiers()
    {
        var (depressed, latched, locked, group) = _seat.Keyboard.ModifierState;
        _router.Modifiers(depressed, latched, locked, group);
    }
}
