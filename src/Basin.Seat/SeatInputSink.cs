using Basin.Capabilities;
using Wayland;
using Xkb;

namespace Basin.Seat;

/// <summary>
/// The default <see cref="IInputSink"/>. It feeds virtual device input into the notify methods of one seat.
/// With a layout, it keeps a layout pointer and maps absolute input onto the layout bounds. It does no hit test, so it never changes the pointer focus.
/// </summary>
public class SeatInputSink : IInputSink
{
    private WlPointer.AxisSource _axisSource = WlPointer.AxisSource.Wheel;
    private OutputLayout? _layout;
    private LayoutPointer? _pointer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeatInputSink"/> class.
    /// </summary>
    /// <param name="seat">The seat that receives the input.</param>
    /// <param name="layout">The layout that absolute pointer and touch input maps onto, or <see langword="null"/> to decline that input.</param>
    public SeatInputSink(Seat seat, OutputLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(seat);
        Seat = seat;
        UseLayout(layout);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SeatInputSink"/> class with no seat and no layout. The host binds both later.
    /// </summary>
    protected SeatInputSink()
    {
        Seat = null!;
    }

    /// <summary>
    /// Gets the seat that receives the input. It is null until the host binds a seat to a sink made with the protected constructor.
    /// </summary>
    protected Seat Seat { get; private set; }

    internal void Bind(Seat seat, OutputLayout? layout)
    {
        Seat ??= seat;
        UseLayout(layout);
    }

    private void UseLayout(OutputLayout? layout)
    {
        if (_layout is null && layout is not null)
        {
            _layout = layout;
            _pointer = new LayoutPointer(layout);
        }
    }

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
    /// Moves the pointer by a distance in layout coordinates and sends the result to the current focus. It does not change the focus.
    /// With no layout, it moves the pointer from its last surface-local position on the focus.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The horizontal distance.</param>
    /// <param name="dy">The vertical distance.</param>
    /// <returns><see langword="false"/> when a layout is set and no surface has the pointer focus.</returns>
    public virtual bool PointerMotion(uint timeMs, double dx, double dy)
    {
        if (_pointer is not { } pointer)
        {
            Seat.Pointer.NotifyMotion(timeMs, Seat.Pointer.X + dx, Seat.Pointer.Y + dy);
            return true;
        }

        if (Seat.Pointer.Focus is not null)
        {
            pointer.Warp(Seat.Pointer.LayoutX, Seat.Pointer.LayoutY);
        }

        pointer.Motion(dx, dy);
        return SendToFocus(timeMs, pointer);
    }

    /// <summary>
    /// Moves the pointer to a point in the extent, which maps onto the layout bounds, and sends the result to the current focus. It does not change the focus.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in the extent, from the left edge of the layout bounds.</param>
    /// <param name="y">The vertical position in the extent, from the top edge of the layout bounds.</param>
    /// <param name="width">The extent width. It must be positive.</param>
    /// <param name="height">The extent height. It must be positive.</param>
    /// <returns><see langword="false"/> when the extent is empty, no layout is set, or no surface has the pointer focus.</returns>
    public virtual bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0 || _pointer is not { } pointer)
        {
            return false;
        }

        pointer.MotionAbsolute(null, x / width, y / height);
        return SendToFocus(timeMs, pointer);
    }

    private bool SendToFocus(uint timeMs, LayoutPointer pointer)
    {
        var pointerState = Seat.Pointer;
        if (pointerState.Focus is not { IsDestroyed: false } focus)
        {
            return false;
        }

        if (pointerState.MapToSurface is not { } map || !map(focus, pointer.X, pointer.Y, out var surfaceX, out var surfaceY))
        {
            surfaceX = pointer.X - (pointerState.LayoutX - pointerState.X);
            surfaceY = pointer.Y - (pointerState.LayoutY - pointerState.Y);
        }

        pointerState.NotifyMotionAt(timeMs, focus, surfaceX, surfaceY, pointer.X, pointer.Y);
        return true;
    }

    private bool ToLayout(double x, double y, double width, double height, out double layoutX, out double layoutY)
    {
        layoutX = 0;
        layoutY = 0;
        if (width <= 0 || height <= 0 || _layout?.Bounds is not { IsEmpty: false } bounds)
        {
            return false;
        }

        layoutX = bounds.X + (x / width * bounds.Width);
        layoutY = bounds.Y + (y / height * bounds.Height);
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
    /// Routes a touch down through the seat's <see cref="TouchRouter"/>. The extent maps onto the layout bounds.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position in the extent, from the left edge of the layout bounds.</param>
    /// <param name="y">The vertical position in the extent, from the top edge of the layout bounds.</param>
    /// <param name="width">The extent width. It must be positive.</param>
    /// <param name="height">The extent height. It must be positive.</param>
    /// <returns><see langword="false"/> when the extent or the layout is empty, no layout is set, or the seat has no router.</returns>
    public virtual bool TouchDown(uint timeMs, int id, double x, double y, double width, double height)
    {
        if (Seat.Touch.Router is not { } router || !ToLayout(x, y, width, height, out var layoutX, out var layoutY))
        {
            return false;
        }

        router.Down(timeMs, id, layoutX, layoutY);
        return true;
    }

    /// <summary>
    /// Routes a touch motion through the seat's <see cref="TouchRouter"/>. The extent maps onto the layout bounds.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The contact id.</param>
    /// <param name="x">The horizontal position in the extent, from the left edge of the layout bounds.</param>
    /// <param name="y">The vertical position in the extent, from the top edge of the layout bounds.</param>
    /// <param name="width">The extent width. It must be positive.</param>
    /// <param name="height">The extent height. It must be positive.</param>
    /// <returns><see langword="false"/> when the extent or the layout is empty, no layout is set, or the seat has no router.</returns>
    public virtual bool TouchMotion(uint timeMs, int id, double x, double y, double width, double height)
    {
        if (Seat.Touch.Router is not { } router || !ToLayout(x, y, width, height, out var layoutX, out var layoutY))
        {
            return false;
        }

        router.Motion(timeMs, id, layoutX, layoutY);
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
