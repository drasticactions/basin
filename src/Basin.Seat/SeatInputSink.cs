using Basin.Capabilities;
using Wayland;
using Xkb;

namespace Basin.Seat;

public class SeatInputSink : IInputSink
{
    private WlPointer.AxisSource _axisSource = WlPointer.AxisSource.Wheel;

    public SeatInputSink(Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        Seat = seat;
    }

    protected SeatInputSink()
    {
        Seat = null!;
    }

    protected Seat Seat { get; private set; }

    internal void Bind(Seat seat) => Seat ??= seat;

    public virtual IInjectedKeyboard? CreateKeyboard() => Seat is { } seat ? seat.Keyboard.CreateDevice() : null;

    public virtual bool Key(IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed)
    {
        Seat.Keyboard.Activate(keyboard);
        Seat.Keyboard.NotifyKey(timeMs, keycode, pressed);
        return true;
    }

    public virtual bool Modifiers(IInjectedKeyboard? keyboard, uint depressed, uint latched, uint locked, uint group)
    {
        Seat.Keyboard.Activate(keyboard);
        Seat.Keyboard.NotifyModifiers(depressed, latched, locked, group);
        return true;
    }

    public virtual bool PointerMotion(uint timeMs, double dx, double dy)
    {
        Seat.Pointer.NotifyMotion(timeMs, Seat.Pointer.X + dx, Seat.Pointer.Y + dy);
        return true;
    }

    public virtual bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        Seat.Pointer.NotifyMotion(timeMs, x, y);
        return true;
    }

    public virtual bool PointerButton(uint timeMs, uint button, bool pressed)
    {
        Seat.Pointer.NotifyButton(timeMs, button, pressed);
        return true;
    }

    public virtual bool PointerAxis(uint timeMs, uint axis, double value)
    {
        Seat.Pointer.NotifyAxis(timeMs, new Basin.PointerAxis((WlPointer.Axis)axis, value, Source: _axisSource));
        return true;
    }

    public virtual bool PointerAxisSource(uint source)
    {
        _axisSource = (WlPointer.AxisSource)source;
        return true;
    }

    public virtual bool PointerAxisStop(uint timeMs, uint axis)
    {
        Seat.Pointer.NotifyAxis(timeMs, new Basin.PointerAxis((WlPointer.Axis)axis, 0, Source: _axisSource));
        return true;
    }

    public virtual bool Frame()
    {
        Seat.Pointer.NotifyFrame();
        return true;
    }

    public virtual bool TouchDown(uint timeMs, int id, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0 || Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Down(timeMs, id, x, y);
        return true;
    }

    public virtual bool TouchMotion(uint timeMs, int id, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0 || Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Motion(timeMs, id, x, y);
        return true;
    }

    public virtual bool TouchUp(uint timeMs, int id)
    {
        if (Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Up(timeMs, id);
        return true;
    }

    public virtual bool TouchFrame()
    {
        if (Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Frame();
        return true;
    }

    public virtual bool TouchCancel()
    {
        if (Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Cancel();
        return true;
    }
}
