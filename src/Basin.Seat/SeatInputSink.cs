using Basin.Capabilities;
using Wayland;
using Xkb;

namespace Basin.Seat;

/// <summary>
/// The default <see cref="IInputSink"/>. It feeds virtual device input straight into the notify methods of one seat, with no hit test.
/// </summary>
public class SeatInputSink : IInputSink
{
    private WlPointer.AxisSource _axisSource = WlPointer.AxisSource.Wheel;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeatInputSink"/> class.
    /// </summary>
    /// <param name="seat">The seat that receives the input.</param>
    public SeatInputSink(Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        Seat = seat;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SeatInputSink"/> class with no seat. The host binds the seat later.
    /// </summary>
    protected SeatInputSink()
    {
        Seat = null!;
    }

    /// <summary>
    /// Gets the seat that receives the input. It is null until the host binds a seat to a sink made with the protected constructor.
    /// </summary>
    protected Seat Seat { get; private set; }

    internal void Bind(Seat seat) => Seat ??= seat;

    /// <summary>
    /// Creates a <see cref="KeyboardDevice"/> on the seat keyboard.
    /// </summary>
    /// <returns>The device, or <see langword="null"/> when no seat is bound.</returns>
    public virtual IInjectedKeyboard? CreateKeyboard() => Seat is { } seat ? seat.Keyboard.CreateDevice() : null;

    /// <summary>
    /// Makes the device the active keyboard, then notifies the key to the seat.
    /// </summary>
    /// <param name="keyboard">The device, or <see langword="null"/> for the seat keymap.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="keycode">The Linux evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public virtual bool Key(IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed)
    {
        Seat.Keyboard.Activate(keyboard);
        Seat.Keyboard.NotifyKey(timeMs, keycode, pressed);
        return true;
    }

    /// <summary>
    /// Makes the device the active keyboard, then sets its modifier state.
    /// </summary>
    /// <param name="keyboard">The device, or <see langword="null"/> for the seat keymap.</param>
    /// <param name="depressed">The depressed modifier mask.</param>
    /// <param name="latched">The latched modifier mask.</param>
    /// <param name="locked">The locked modifier mask.</param>
    /// <param name="group">The effective layout index.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public virtual bool Modifiers(IInjectedKeyboard? keyboard, uint depressed, uint latched, uint locked, uint group)
    {
        Seat.Keyboard.Activate(keyboard);
        Seat.Keyboard.NotifyModifiers(depressed, latched, locked, group);
        return true;
    }

    /// <summary>
    /// Moves the pointer by a distance from its last surface-local position on the current focus. It does not change the focus.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The horizontal distance.</param>
    /// <param name="dy">The vertical distance.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public virtual bool PointerMotion(uint timeMs, double dx, double dy)
    {
        Seat.Pointer.NotifyMotion(timeMs, Seat.Pointer.X + dx, Seat.Pointer.Y + dy);
        return true;
    }

    /// <summary>
    /// Sends the point as a surface-local motion on the current focus. The extent is checked but not used to scale the point.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <param name="width">The extent width. It must be positive.</param>
    /// <param name="height">The extent height. It must be positive.</param>
    /// <returns><see langword="false"/> when the extent is empty.</returns>
    public virtual bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        Seat.Pointer.NotifyMotion(timeMs, x, y);
        return true;
    }

    /// <summary>
    /// Notifies a button to the seat pointer.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The Linux evdev button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public virtual bool PointerButton(uint timeMs, uint button, bool pressed)
    {
        Seat.Pointer.NotifyButton(timeMs, button, pressed);
        return true;
    }

    /// <summary>
    /// Notifies a scroll to the seat pointer with the last axis source, which starts as a wheel.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The <c>wl_pointer.axis</c> value.</param>
    /// <param name="value">The scroll distance. Zero ends the scroll.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public virtual bool PointerAxis(uint timeMs, uint axis, double value)
    {
        Seat.Pointer.NotifyAxis(timeMs, new Basin.PointerAxis((WlPointer.Axis)axis, value, Source: _axisSource));
        return true;
    }

    /// <summary>
    /// Sets the axis source for the scroll events that follow. It stays until the next call.
    /// </summary>
    /// <param name="source">The <c>wl_pointer.axis_source</c> value.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public virtual bool PointerAxisSource(uint source)
    {
        _axisSource = (WlPointer.AxisSource)source;
        return true;
    }

    /// <summary>
    /// Notifies a scroll of zero, which the seat sends as <c>axis_stop</c>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The <c>wl_pointer.axis</c> value.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public virtual bool PointerAxisStop(uint timeMs, uint axis)
    {
        Seat.Pointer.NotifyAxis(timeMs, new Basin.PointerAxis((WlPointer.Axis)axis, 0, Source: _axisSource));
        return true;
    }

    /// <summary>
    /// Sends <c>wl_pointer.frame</c> to the focused client.
    /// </summary>
    /// <returns>Always <see langword="true"/>.</returns>
    public virtual bool Frame()
    {
        Seat.Pointer.NotifyFrame();
        return true;
    }

    /// <summary>
    /// Routes a touch down through the seat's <see cref="TouchRouter"/>. The point is used as a layout position, not scaled to the extent.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <param name="width">The extent width. It must be positive.</param>
    /// <param name="height">The extent height. It must be positive.</param>
    /// <returns><see langword="false"/> when the extent is empty or the seat has no router.</returns>
    public virtual bool TouchDown(uint timeMs, int id, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0 || Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Down(timeMs, id, x, y);
        return true;
    }

    /// <summary>
    /// Routes a touch motion through the seat's <see cref="TouchRouter"/>. The point is used as a layout position, not scaled to the extent.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position in layout coordinates.</param>
    /// <param name="y">The vertical position in layout coordinates.</param>
    /// <param name="width">The extent width. It must be positive.</param>
    /// <param name="height">The extent height. It must be positive.</param>
    /// <returns><see langword="false"/> when the extent is empty or the seat has no router.</returns>
    public virtual bool TouchMotion(uint timeMs, int id, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0 || Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Motion(timeMs, id, x, y);
        return true;
    }

    /// <summary>
    /// Routes a touch lift through the seat's <see cref="TouchRouter"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <returns><see langword="false"/> when the seat has no router.</returns>
    public virtual bool TouchUp(uint timeMs, int id)
    {
        if (Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Up(timeMs, id);
        return true;
    }

    /// <summary>
    /// Ends a group of touch events through the seat's <see cref="TouchRouter"/>.
    /// </summary>
    /// <returns><see langword="false"/> when the seat has no router.</returns>
    public virtual bool TouchFrame()
    {
        if (Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Frame();
        return true;
    }

    /// <summary>
    /// Cancels every contact through the seat's <see cref="TouchRouter"/>.
    /// </summary>
    /// <returns><see langword="false"/> when the seat has no router.</returns>
    public virtual bool TouchCancel()
    {
        if (Seat.Touch.Router is not { } router)
        {
            return false;
        }

        router.Cancel();
        return true;
    }

    /// <summary>
    /// Gives keyboard and pointer, and touch when the seat has a <see cref="TouchRouter"/>.
    /// </summary>
    /// <returns>The kinds of input that the sink can deliver now.</returns>
    public virtual InputDeviceCapability Supports() =>
        InputDeviceCapability.Keyboard | InputDeviceCapability.Pointer |
        (Seat is { Touch.Router: not null } ? InputDeviceCapability.Touch : InputDeviceCapability.None);
}
