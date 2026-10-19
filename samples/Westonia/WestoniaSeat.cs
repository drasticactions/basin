using Basin;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Scene;
using Basin.Seat;
using Basin.UI.Avalonia;

using Basin.Diagnostics;

namespace Westonia;

internal sealed class WestoniaSeat : IDisposable
{
    private readonly Basin.Host.BasinHost _host;
    private readonly Seat _seat;
    private readonly OutputLayout _layout;
    private readonly Scene _scene;
    private readonly CursorController _cursor;
    private readonly LayoutPointer _pointer;
    private readonly AvaloniaShell _shell;
    private readonly UISurfaceRouter _router;
    private readonly WestonShell _policy;
    private readonly BasinLogger _log;
    private readonly Basin.Seat.Backends.SeatBinder _binder;
    private readonly Basin.Seat.Backends.SeatInjector _injector;
    private readonly Basin.Seat.Backends.SeatTouchDriver _touch;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private readonly PointerRefresh _pointerRefresh;
    private IUISurface? _routeSurface;
    private ButtonRoute _route;
    private readonly Basin.Seat.Backends.ButtonLatch _latch;
    private readonly Basin.Seat.Backends.ChromeKeyDelivery _keys;
    private double _x;
    private double _y;
    private bool _disposed;

    public WestoniaSeat(
        Basin.Host.BasinHost host,
        BasinServices services,
        OutputLayout layout,
        Scene scene,
        CursorController cursor,
        AvaloniaShell shell,
        UISurfaceIndex index,
        WestonShell policy,
        Basin.Backend.Libinput.LibinputBackend? input,
        Basin.Seat.Backends.HookInputSink inputSink,
        BasinLogger log)
    {
        _host = host;
        _seat = services.Require<Seat>();
        _layout = layout;
        _scene = scene;
        _cursor = cursor;
        _shell = shell;
        _router = new UISurfaceRouter(scene, index);
        _latch = new Basin.Seat.Backends.ButtonLatch(_seat);
        _keys = new Basin.Seat.Backends.ChromeKeyDelivery(_seat, _router);
        _policy = policy;
        _log = log;
        _input = input;
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
        _touch.Router.Activity =
            services.Find<Basin.Capabilities.IIdleSource>() as Basin.Seat.SeatIdleSource;

        _binder.RelativePointer = services.Find<Basin.Desktop.RelativePointerManager>();
        _binder.Constraints = services.Find<Basin.Desktop.PointerConstraintDriver>();
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
            _log.Info($"libinput started on seat {(host.Session?.SeatName ?? "seat0")}");
        }
    }

    public Func<uint, uint, bool, bool>? KeyHook { get; set; }

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

    public (double X, double Y) Position => (_x, _y);

    public Action<(double X, double Y)>? PointerMoved { get; set; }

    public bool IsOverShell => _router.Hovered is not null;

    public IUISurface? Hovered => _router.Hovered;

    public UISurfaceRouter Router => _router;

    public Basin.Seat.Backends.SeatBinder Binder => _binder;

    public Basin.Seat.Backends.InputConfigurator? Configurator { get; set; }

    public double PointerX => _x;

    public double PointerY => _y;

    public void CenterPointer() => _injector.Center();

    public Basin.Seat.Backends.SeatInjector Injector => _injector;

    public void Dispose()
    {
        _disposed = true;
        _pointerRefresh.Dispose();
    }

    internal void RefreshPointer() => MoveCursor((uint)Environment.TickCount);

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
        PointerMoved?.Invoke((_x, _y));
        if (CaptureHook?.Invoke(time, _x, _y, dx, dy) == true)
        {
            return;
        }

        if (_policy.Grab.Active)
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

        if (FrameCursorAt?.Invoke(_x, _y) is { } frameCursor)
        {
            _cursor.SetHover(null, overClient: false);
            _cursor.ShowNamed(frameCursor);
        }
        else
        {
            _cursor.SetHover(hit.Surface, overClient: true);
        }

        _seat.Pointer.NotifyMotionAt(time, hit.Surface, hit.X, hit.Y, _x, _y);
    }

    private enum ButtonRoute
    {
        Seat,

        Frame,

        Shell,
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
        if (FrameCursorAt is not null && _router.Hovered is { } framed && OwnsFrame(framed))
        {
            _routeSurface = framed;
            return ButtonRoute.Frame;
        }

        if (_router.Hovered is { } surface)
        {
            _routeSurface = surface;
            return ButtonRoute.Shell;
        }

        _routeSurface = null;
        return ButtonRoute.Seat;
    }

    private bool OwnsFrame(IUISurface surface)
    {
        foreach (var window in _policy.Windows)
        {
            if (window.Frame?.OwnsSurface(surface) == true)
            {
                return true;
            }
        }

        return false;
    }

    private void Dispatch(uint time, uint button, bool pressed)
    {
        var grabbed = _policy.Grab.Active;

        if (_route != ButtonRoute.Seat)
        {
            if (grabbed)
            {
                ButtonHook?.Invoke(time, button, pressed);
                return;
            }

            if (_route == ButtonRoute.Frame && FrameButtonHook?.Invoke(_x, _y, button, pressed) == true)
            {
                return;
            }

            if (_routeSurface is { } target)
            {
                _router.PointerButton(time, button, pressed, target);
            }

            return;
        }

        if (pressed)
        {
            if (ButtonHook?.Invoke(time, button, pressed) == true)
            {
                return;
            }

            _latch.SeatButton(time, button, true);
            return;
        }

        ButtonHook?.Invoke(time, button, false);
        _latch.SeatButton(time, button, false);
    }

    public Func<uint, uint, bool, bool>? ButtonHook { get; set; }

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

    public Func<double, double, uint, bool, bool>? FrameButtonHook { get; set; }

    public Func<uint, double, double, double, double, bool>? CaptureHook { get; set; }

    public void Warp(double x, double y) => _pointer.Warp(x, y);

    public Func<double, double, string?>? FrameCursorAt { get; set; }

}
