using Basin;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Scene;
using Basin.Seat;
using Basin.UI.Avalonia;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger _log;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private IUISurface? _routeSurface;
    private ButtonRoute _route;
    private int _buttonsDown;
    private bool _seatHoldsButton;
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
        ILogger log)
    {
        _host = host;
        _seat = services.Require<Seat>();
        _layout = layout;
        _scene = scene;
        _cursor = cursor;
        _shell = shell;
        _router = new UISurfaceRouter(scene, index);
        _policy = policy;
        _log = log;
        _input = input;
        _pointer = new LayoutPointer(layout);
        _pointer.Moved += () => MoveCursor((uint)Environment.TickCount);

        if (host.Parent is { } parent)
        {
            parent.PointerAdded += WireParentPointer;
            parent.KeyboardAdded += WireParentKeyboard;
        }

        if (input is not null)
        {
            WireLibinput(input);
            input.Start();
            _log.LogInformation("libinput started on seat {Seat}", host.Session?.SeatName ?? "seat0");
        }
    }

    public Func<uint, uint, bool, bool>? KeyHook { get; set; }

    public Action<(double X, double Y)>? PointerMoved { get; set; }

    public bool IsOverShell => _router.Hovered is not null;

    public IUISurface? Hovered => _router.Hovered;

    public UISurfaceRouter Router => _router;

    public double PointerX => _x;

    public double PointerY => _y;

    public void CenterPointer()
    {
        var bounds = _layout.Bounds;
        _pointer.Warp(bounds.X + (bounds.Width / 2.0), bounds.Y + (bounds.Height / 2.0));
    }

    public void WarpPointer(double x, double y)
    {
        _pointer.Warp(x, y);
        MoveCursor((uint)Environment.TickCount);
    }

    public void InjectButton(uint button, bool pressed) =>
        OnButton((uint)Environment.TickCount, button, pressed);

    public void InjectKey(uint key, bool pressed) =>
        OnKey((uint)Environment.TickCount, key, pressed);

    public void Dispose() => _disposed = true;

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
            var output = _layout.OutputAt(_x, _y) ??
                (_layout.Outputs.Count > 0 ? _layout.Outputs[0].Output : null);
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
            OnModifiers(depressed, latched, locked, group);
        keyboard.Leave += () => _seat.Keyboard.NotifyClearFocus();
    }

    private void WireLibinput(Basin.Backend.Libinput.LibinputBackend input)
    {
        input.DeviceAdded += device =>
        {
            Console.WriteLine($"INPUT + {device.Name}");
            _seat.SetCapability(SeatCapability.Touch, input.HasTouchDevice);
        };
        input.DeviceRemoved += device =>
        {
            Console.WriteLine($"INPUT - {device.Name}");
            _seat.SetCapability(SeatCapability.Touch, input.HasTouchDevice);
        };
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
        input.TouchDown += (device, time, slot, nx, ny) =>
        {
            var (x, y) = TouchToLayout(nx, ny);
            OnTouchDown(time, slot, x, y);
        };
        input.TouchMotion += (device, time, slot, nx, ny) =>
        {
            var (x, y) = TouchToLayout(nx, ny);
            OnTouchMotion(time, slot, x, y);
        };
        input.TouchUp += (_, time, slot) => OnTouchUp(time, slot);
        input.TouchFrame += _ => _seat.Touch.NotifyFrame();
        input.TouchCancel += _ => OnTouchCancel();
    }

    private (double X, double Y) TouchToLayout(double normalizedX, double normalizedY)
    {
        var bounds = _layout.Bounds;
        return (bounds.X + (normalizedX * bounds.Width), bounds.Y + (normalizedY * bounds.Height));
    }

    private void OnTouchDown(uint time, int slot, double x, double y)
    {
        _pointer.Warp(x, y);
        MoveCursor(time);
        if (_router.TouchDown(time, slot, x, y))
        {
            return;
        }

        if (_scene.SurfaceAt(x, y) is { Surface: { } client } hit)
        {
            _seat.Touch.NotifyDown(client, time, slot, hit.X, hit.Y);
        }
    }

    private void OnTouchMotion(uint time, int slot, double x, double y)
    {
        _pointer.Warp(x, y);
        MoveCursor(time);
        if (_router.TouchMotion(time, slot, x, y))
        {
            return;
        }

        _seat.Touch.NotifyMotion(time, slot, x, y);
    }

    private void OnTouchUp(uint time, int slot)
    {
        if (_router.TouchUp(time, slot))
        {
            return;
        }

        _seat.Touch.NotifyUp(time, slot);
    }

    private void OnTouchCancel()
    {
        _router.TouchCancel();
        _seat.Touch.NotifyCancel();
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
        PointerMoved?.Invoke((_x, _y));
        if (_policy.Grab.Active)
        {
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
        if (pressed)
        {
            if (_buttonsDown++ == 0)
            {
                _route = ChooseRoute();
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
            _route = ButtonRoute.Seat;
            _routeSurface = null;
            _seatHoldsButton = false;
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

    public Func<uint, uint, bool, bool>? ButtonHook { get; set; }

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

        if (_router.Key(time, key, pressed))
        {
            return;
        }

        _seat.Keyboard.NotifyKey(time, key, pressed);
    }

    private void OnModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        _router.Modifiers(depressed, latched, locked, group);
        _seat.Keyboard.NotifyModifiers(depressed, latched, locked, group);
    }

    public Func<double, double, uint, bool, bool>? FrameButtonHook { get; set; }

    public Func<double, double, string?>? FrameCursorAt { get; set; }

}
