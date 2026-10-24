using Basin;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Seat;

namespace MauiComp;

internal sealed class MauiCompSeat : IDisposable
{
    private readonly Seat _seat;
    private readonly OutputLayout _layout;
    private readonly Scene _scene;
    private readonly CursorController _cursor;
    private readonly LayoutPointer _pointer;
    private readonly UISurfaceRouter _router;
    private readonly Basin.Seat.Backends.SeatBinder _binder;
    private readonly Basin.Seat.Backends.SeatInjector _injector;
    private readonly Basin.Seat.Backends.SeatTouchDriver _touch;
    private readonly PointerRefresh _pointerRefresh;
    private readonly BasinLogger _log;
    private IUISurface? _routeSurface;
    private bool _routeToShell;
    private readonly Basin.Seat.Backends.ButtonLatch _latch;
    private readonly Basin.Seat.Backends.ChromeKeyDelivery _keys;
    private double _x;
    private double _y;
    private bool _disposed;

    public MauiCompSeat(
        Basin.Host.BasinHost host,
        Seat seat,
        OutputLayout layout,
        Scene scene,
        CursorController cursor,
        UISurfaceIndex index,
        Basin.Backend.Libinput.LibinputBackend? input,
        Basin.Seat.Backends.HookInputSink inputSink,
        BasinLogger log)
    {
        _seat = seat;
        _layout = layout;
        _scene = scene;
        _cursor = cursor;
        _log = log;
        _router = new UISurfaceRouter(scene, index);
        _latch = new Basin.Seat.Backends.ButtonLatch(_seat);
        _keys = new Basin.Seat.Backends.ChromeKeyDelivery(_seat, _router);
        _pointer = new LayoutPointer(layout);
        _pointer.Moved += () => MoveCursor((uint)Environment.TickCount);
        _pointerRefresh = new PointerRefresh(scene, host.Loop, () => MoveCursor((uint)Environment.TickCount));

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
            _log.Info($"input: libinput started on seat {(host.Session?.SeatName ?? "seat0")}");
        }
    }

    public Basin.Seat.Backends.InputConfigurator? Configurator { get; set; }

    public Func<uint, uint, bool, bool>? KeyHook { get; set; }

    public Func<uint, uint, bool, bool>? ButtonHook { get; set; }

    public Func<uint, double, double, double, double, bool>? CaptureHook { get; set; }

    public void Warp(double x, double y) => _pointer.Warp(x, y);

    public Func<IUISurface?, bool>? ChromeClick { get; set; }

    public Func<double, double, string?>? ChromeCursor { get; set; }

    public Action<double, double>? PointerMoved { get; set; }

    public Func<bool>? Grabbing { get; set; }

    private readonly Basin.Seat.Backends.TouchChromeChain _touchChrome = new();
    private Basin.Inspector.InspectorInput? _inspector;

    public Basin.Inspector.InspectorInput? Inspector
    {
        get => _inspector;
        set
        {
            _inspector = value;
            _touchChrome.First = value;
        }
    }

    public bool IsOverShell => _router.Hovered is not null;

    public UISurfaceRouter Router => _router;

    public double PointerX => _x;

    public double PointerY => _y;

    public Basin.Seat.Backends.SeatInjector Injector => _injector;

    public Basin.Seat.Backends.SeatBinder Binder => _binder;

    public void CenterPointer() => _injector.Center();

    public void RefreshPointer() => MoveCursor((uint)Environment.TickCount);

    public void Dispose()
    {
        _disposed = true;
        _pointerRefresh.Dispose();
    }

    private void MoveCursor(uint time)
    {
        if (_disposed)
        {
            return;
        }

        var dx = _pointer.X - _x;
        var dy = _pointer.Y - _y;
        _x = _pointer.X;
        _y = _pointer.Y;
        _cursor.MoveTo(_x, _y);
        PointerMoved?.Invoke(_x, _y);
        if (CaptureHook?.Invoke(time, _x, _y, dx, dy) == true)
        {
            return;
        }

        if (Grabbing?.Invoke() == true)
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

        if (ChromeCursor?.Invoke(_x, _y) is { } chromeCursor)
        {
            _router.PointerLeave();
            _seat.Pointer.NotifyClearFocus();
            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed(chromeCursor);
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
            _cursor.ShowNamed(route.Cursor ?? "default");
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
                _routeSurface = _router.Hovered;
                _routeToShell = _routeSurface is not null;
                if (ChromeClick?.Invoke(_routeSurface) == true)
                {
                    _routeSurface = null;
                    _routeToShell = false;
                    return;
                }
            }

            Dispatch(time, button, pressed: true);
            return;
        }

        Dispatch(time, button, pressed: false);
        if (_latch.Release())
        {
            _routeToShell = false;
            _routeSurface = null;
        }
    }

    private void Dispatch(uint time, uint button, bool pressed)
    {
        if (_routeToShell)
        {
            if (_routeSurface is { } target)
            {
                _router.PointerButton(time, button, pressed, target);
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

    private void OnAxis(uint time, PointerAxis axis)
    {
        if (Inspector?.Axis(time, axis) == true)
        {
            return;
        }

        if (_router.PointerAxis(time, axis))
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

        if (Inspector?.Key(time, key, pressed) == true)
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
