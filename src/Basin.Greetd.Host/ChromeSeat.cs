using Basin;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Seat;
using Basin.Capabilities;

namespace Basin.Greetd.Host;

public sealed class ChromeSeat : IDisposable
{
    private readonly Basin.Seat.Seat _seat;
    private readonly CursorController _cursor;
    private readonly LayoutPointer _pointer;
    private readonly UISurfaceRouter _router;
    private readonly Basin.Seat.Backends.SeatBinder _binder;
    private readonly Basin.Seat.Backends.SeatInjector _injector;
    private readonly Basin.Seat.Backends.ChromeKeyDelivery _keys;
    private IUISurface? _pressed;
    private int _buttons;
    private double _x;
    private double _y;
    private bool _disposed;

    public ChromeSeat(
        Basin.Host.BasinHost host,
        Basin.Seat.Seat seat,
        OutputLayout layout,
        Basin.Scene.Scene scene,
        UISurfaceIndex index,
        CursorController cursor,
        Basin.Backend.Libinput.LibinputBackend? input,
        BasinLogger log)
    {
        _seat = seat;
        _cursor = cursor;
        _router = new UISurfaceRouter(scene, index);
        _keys = new Basin.Seat.Backends.ChromeKeyDelivery(seat, _router);
        _pointer = new LayoutPointer(layout);
        _pointer.Moved += () => Move((uint)Environment.TickCount);
        _binder = new Basin.Seat.Backends.SeatBinder(seat, layout, _pointer, cursor);
        _injector = new Basin.Seat.Backends.SeatInjector(_binder, seat, layout, _pointer)
        {
            DeliverButton = OnButton,
            DeliverKey = OnKey,
        };
        _binder.Key += OnKey;
        _binder.ModifiersChanged += OnModifiers;
        _binder.Button += OnButton;
        _binder.Axis += (time, axis) => _router.PointerAxis(time, axis);
        _binder.PointerLeft += _router.PointerLeave;
        _binder.TouchDown += OnTouchDown;
        _binder.TouchMotion += (time, id, x, y) => _router.TouchMotion(time, id, x, y);
        _binder.TouchUp += (time, id) => _router.TouchUp(time, id);
        _binder.TouchCancelled += () => _router.TouchCancel();
        _binder.TouchFrame += () => TouchFramed?.Invoke();
        if (host.Parent is { } parent)
        {
            _binder.BindParent(parent);
        }

        if (input is not null)
        {
            input.DeviceAdded += device => BasinReport.Line($"INPUT + {device.Name}");
            input.DeviceRemoved += device => BasinReport.Line($"INPUT - {device.Name}");
            _binder.BindLibinput(input);
            log.Info($"libinput started on seat {host.Session?.SeatName ?? "seat0"}");
        }
    }

    public UISurfaceRouter Router => _router;

    public Basin.Seat.Backends.SeatBinder Binder => _binder;

    public Basin.Seat.Backends.SeatInjector Injector => _injector;

    public double PointerX => _x;

    public double PointerY => _y;

    public Func<uint, uint, bool, bool>? KeyHook { get; set; }

    public Action<double, double>? PointerMoved { get; set; }

    public Action<double, double>? Touched { get; set; }

    public Action? Pressed { get; set; }

    public Action? TouchFramed { get; set; }

    public void Warp(double x, double y) => _pointer.Warp(x, y);

    public void Center() => _injector.Center();

    public void Dispose() => _disposed = true;

    private void Move(uint time)
    {
        if (_disposed)
        {
            return;
        }

        _x = _pointer.X;
        _y = _pointer.Y;
        _cursor.MoveTo(_x, _y);
        PointerMoved?.Invoke(_x, _y);
        var route = _router.PointerMotion(time, _x, _y);
        _cursor.ShowNamed(route.Surface is not null ? route.Cursor ?? "default" : "default");
    }

    private void OnButton(uint time, uint button, bool pressed)
    {
        if (pressed)
        {
            Pressed?.Invoke();
            if (_buttons++ == 0)
            {
                _pressed = _router.Hovered;
            }

            _router.PointerButton(time, button, true, _pressed);
            return;
        }

        _router.PointerButton(time, button, false, _pressed);
        if (_buttons > 0 && --_buttons == 0)
        {
            _pressed = null;
        }
    }

    private void OnKey(uint time, uint key, bool pressed)
    {
        if (pressed)
        {
            Pressed?.Invoke();
        }

        if (KeyHook?.Invoke(time, key, pressed) == true)
        {
            _seat.Keyboard.NotifyKeyConsumed(key, pressed);
            return;
        }

        if (!_keys.Chrome(time, key, pressed))
        {
            _seat.Keyboard.NotifyKeyConsumed(key, pressed);
        }
    }

    private void OnModifiers()
    {
        var (depressed, latched, locked, group) = _seat.Keyboard.ModifierState;
        _router.Modifiers(depressed, latched, locked, group);
    }

    private void OnTouchDown(uint time, int id, double x, double y)
    {
        Touched?.Invoke(x, y);
        _router.TouchDown(time, id, x, y);
    }
}
