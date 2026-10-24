using Basin;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Seat;
using Basin.Capabilities;

namespace Basin.Greetd.Host;

/// <summary>
/// The seat of a compositor whose only input target is its own chrome. It routes libinput and the nested parent to the UI surfaces and exposes hooks for each kind of input.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromeSeat"/> class. It binds the nested parent of <paramref name="host"/> and <paramref name="input"/> at once.
    /// </summary>
    /// <param name="host">The host whose parent, if any, delivers input.</param>
    /// <param name="seat">The seat whose keyboard holds the keymap and the modifiers.</param>
    /// <param name="layout">The output layout for the pointer.</param>
    /// <param name="scene">The scene that the router searches for a surface.</param>
    /// <param name="index">The index of the UI surfaces in the scene.</param>
    /// <param name="cursor">The cursor that follows the pointer.</param>
    /// <param name="input">The libinput backend, or <see langword="null"/> with no local devices. Each device change prints an <c>INPUT</c> line.</param>
    /// <param name="log">The logger for the start of libinput.</param>
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
        _binder.Axis += OnAxis;
        _binder.PointerLeft += OnPointerLeft;
        _binder.TouchDown += OnTouchDown;
        _binder.TouchMotion += OnTouchMotion;
        _binder.TouchUp += OnTouchUp;
        _binder.TouchCancelled += OnTouchCancelled;
        _binder.TouchFrame += OnTouchFrame;
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

    /// <summary>
    /// Gets the router that delivers pointer, touch, key and text input to the UI surfaces.
    /// </summary>
    public UISurfaceRouter Router => _router;

    /// <summary>
    /// Gets the binder that turns the device events into seat events.
    /// </summary>
    public Basin.Seat.Backends.SeatBinder Binder => _binder;

    /// <summary>
    /// Gets the injector for synthetic input. Its keys and buttons go through the same hooks as real input.
    /// </summary>
    public Basin.Seat.Backends.SeatInjector Injector => _injector;

    /// <summary>
    /// Gets the pointer x position in layout coordinates after the last motion.
    /// </summary>
    public double PointerX => _x;

    /// <summary>
    /// Gets the pointer y position in layout coordinates after the last motion.
    /// </summary>
    public double PointerY => _y;

    /// <summary>
    /// Gets or sets a hook that sees each key before the chrome. Its arguments are time, evdev key code and pressed. A return of <see langword="true"/> takes the key.
    /// </summary>
    public Func<uint, uint, bool, bool>? KeyHook { get; set; }

    /// <summary>
    /// Gets or sets a hook that receives each pointer position in layout coordinates.
    /// </summary>
    public Action<double, double>? PointerMoved { get; set; }

    /// <summary>
    /// Gets or sets a hook that receives the layout position of each touch down.
    /// </summary>
    public Action<double, double>? Touched { get; set; }

    /// <summary>
    /// Gets or sets a hook that runs on each button or key press.
    /// </summary>
    public Action? Pressed { get; set; }

    /// <summary>
    /// Gets or sets a hook that runs at the end of each touch frame.
    /// </summary>
    public Action? TouchFramed { get; set; }

    /// <summary>
    /// Moves the pointer to a layout position and delivers the motion.
    /// </summary>
    /// <param name="x">The x position in layout coordinates.</param>
    /// <param name="y">The y position in layout coordinates.</param>
    public void Warp(double x, double y) => _pointer.Warp(x, y);

    /// <summary>
    /// Moves the pointer to the center of the layout.
    /// </summary>
    public void Center() => _injector.Center();

    /// <summary>
    /// Stops all input delivery and clears the hooks. It does not unbind libinput or the parent, which the host disposes.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _pressed = null;
        KeyHook = null;
        PointerMoved = null;
        Touched = null;
        Pressed = null;
        TouchFramed = null;
    }

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
        if (_disposed)
        {
            return;
        }

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
        if (_disposed)
        {
            return;
        }

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
        if (_disposed)
        {
            return;
        }

        var (depressed, latched, locked, group) = _seat.Keyboard.ModifierState;
        _router.Modifiers(depressed, latched, locked, group);
    }

    private void OnAxis(uint time, PointerAxis axis)
    {
        if (!_disposed)
        {
            _router.PointerAxis(time, axis);
        }
    }

    private void OnPointerLeft()
    {
        if (!_disposed)
        {
            _router.PointerLeave();
        }
    }

    private void OnTouchDown(uint time, int id, double x, double y)
    {
        if (_disposed)
        {
            return;
        }

        Touched?.Invoke(x, y);
        _router.TouchDown(time, id, x, y);
    }

    private void OnTouchMotion(uint time, int id, double x, double y)
    {
        if (!_disposed)
        {
            _router.TouchMotion(time, id, x, y);
        }
    }

    private void OnTouchUp(uint time, int id)
    {
        if (!_disposed)
        {
            _router.TouchUp(time, id);
        }
    }

    private void OnTouchCancelled()
    {
        if (!_disposed)
        {
            _router.TouchCancel();
        }
    }

    private void OnTouchFrame()
    {
        if (!_disposed)
        {
            TouchFramed?.Invoke();
        }
    }
}
