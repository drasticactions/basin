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
    private readonly PointerRefresh _pointerRefresh;
    private readonly BasinLogger _log;
    private Basin.Seat.Backends.StdinInputCommands? _stdinCommands;
    private IUISurface? _routeSurface;
    private bool _routeToShell;
    private int _buttonsDown;
    private bool _seatHoldsButton;
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
        BasinLogger log)
    {
        _seat = seat;
        _layout = layout;
        _scene = scene;
        _cursor = cursor;
        _log = log;
        _router = new UISurfaceRouter(scene, index);
        _pointer = new LayoutPointer(layout);
        _pointer.Moved += () => MoveCursor((uint)Environment.TickCount);
        _pointerRefresh = new PointerRefresh(scene, host.Loop, () => MoveCursor((uint)Environment.TickCount));

        _binder = new Basin.Seat.Backends.SeatBinder(_seat, layout, _pointer, cursor);
        _injector = new Basin.Seat.Backends.SeatInjector(_binder, _seat, layout, _pointer)
        {
            Moved = MoveCursor,
            DeliverButton = OnButton,
            DeliverKey = OnKey,
        };

        if (host.Parent is { } parent)
        {
            parent.PointerAdded += WireParentPointer;
            parent.KeyboardAdded += WireParentKeyboard;
        }

        if (input is not null)
        {
            WireLibinput(input);
            input.Start();
            _log.Info($"libinput started on seat {(host.Session?.SeatName ?? "seat0")}");
        }
    }

    public Func<uint, uint, bool, bool>? KeyHook { get; set; }

    public Func<uint, uint, bool, bool>? ButtonHook { get; set; }

    public Func<IUISurface?, bool>? ChromeClick { get; set; }

    public Func<double, double, string?>? ChromeCursor { get; set; }

    public Action<double, double>? PointerMoved { get; set; }

    public Func<bool>? Grabbing { get; set; }

    public bool IsOverShell => _router.Hovered is not null;

    public UISurfaceRouter Router => _router;

    public double PointerX => _x;

    public double PointerY => _y;

    public Basin.Seat.Backends.StdinInputCommands StdinCommands =>
        _stdinCommands ??= new Basin.Seat.Backends.StdinInputCommands(_injector);

    public void CenterPointer() => _injector.Center();

    public void Dispose()
    {
        _disposed = true;
        _pointerRefresh.Dispose();
    }

    private void WireParentPointer(WaylandPointerDevice pointer)
    {
        _cursor.AttachParent(pointer);
        pointer.Enter += (output, x, y) =>
        {
            var (layoutX, layoutY) = _layout.ToLayout(output, x, y);
            _pointer.Warp(layoutX, layoutY);
        };
        pointer.Motion += (time, x, y) =>
        {
            var outputs = _layout.Outputs;
            var output = _layout.OutputAt(_x, _y) ?? (outputs.Length > 0 ? outputs[0].Output : null);
            if (output is null)
            {
                return;
            }

            var (layoutX, layoutY) = _layout.ToLayout(output, x, y);
            _pointer.Warp(layoutX, layoutY);
            MoveCursor(time);
        };
        pointer.Button += (time, button, pressed) => OnButton(time, button, pressed);
        pointer.Axis += (time, axis) => OnAxis(time, axis);
        pointer.Leave += _router.PointerLeave;
    }

    private void WireParentKeyboard(WaylandKeyboardDevice keyboard)
    {
        keyboard.Keymap += bytes => _seat.Keyboard.SetKeymapFromBuffer(bytes);
        keyboard.Key += (time, key, pressed) => OnKey(time, key, pressed);
        keyboard.Modifiers += (depressed, latched, locked, group) =>
        {
            _router.Modifiers(depressed, latched, locked, group);
            _seat.Keyboard.NotifyModifiers(depressed, latched, locked, group);
        };
        keyboard.Leave += () => _seat.Keyboard.NotifyClearFocus();
    }

    private void WireLibinput(Basin.Backend.Libinput.LibinputBackend input)
    {
        input.DeviceAdded += device => BasinReport.Line($"INPUT + {device.Name}");
        input.DeviceRemoved += device => BasinReport.Line($"INPUT - {device.Name}");
        input.Key += (_, time, key, pressed) => OnKey(time, key, pressed);
        input.PointerButton += (_, time, button, pressed) => OnButton(time, button, pressed);
        input.PointerMotion += (_, time, dx, dy, _, _) =>
        {
            _pointer.Motion(dx, dy);
            MoveCursor(time);
        };
        input.PointerMotionAbsolute += (_, time, nx, ny) =>
        {
            _pointer.MotionAbsolute(null, nx, ny);
            MoveCursor(time);
        };
        input.PointerScroll += (_, time, axis) => OnAxis(time, axis);
    }

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
        if (Grabbing?.Invoke() == true)
        {
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
        if (pressed)
        {
            if (_buttonsDown++ == 0)
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

        if (_buttonsDown > 0)
        {
            _buttonsDown--;
        }

        Dispatch(time, button, pressed: false);
        if (_buttonsDown == 0)
        {
            _routeToShell = false;
            _routeSurface = null;
            _seatHoldsButton = false;
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

            _seat.Pointer.NotifyButton(time, button, true);
            _seatHoldsButton = true;
            return;
        }

        ButtonHook?.Invoke(time, button, false);
        if (_seatHoldsButton)
        {
            _seat.Pointer.NotifyButton(time, button, false);
            _seatHoldsButton = false;
        }
    }

    private void OnAxis(uint time, PointerAxis axis)
    {
        var horizontal = axis.Axis == Wayland.WlPointer.Axis.HorizontalScroll;
        if (_router.PointerAxis(time, horizontal ? axis.Value : 0, horizontal ? 0 : axis.Value))
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

        if (_router.KeyboardFocus is not null)
        {
            _router.Key(time, key, pressed);
            if (pressed && TextOf(key) is { } text)
            {
                _router.TextCommit(text);
            }
        }

        _seat.Keyboard.NotifyKey(time, key, pressed);
    }

    private string? TextOf(uint key)
    {
        if (_seat.Keyboard.State is not { } state ||
            state.IsModActive("Control") || state.IsModActive("Mod1") || state.IsModActive("Mod4"))
        {
            return null;
        }

        var codepoint = state.GetKeyUtf32(key + 8);
        return codepoint is > 0x1f and not 0x7f ? char.ConvertFromUtf32((int)codepoint) : null;
    }
}
