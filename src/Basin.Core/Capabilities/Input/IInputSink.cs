namespace Basin.Capabilities;

/// <summary>
/// Receives input from a virtual device, for example a virtual keyboard client, an EIS sender or a remote desktop session.
/// Keycodes and buttons are Linux evdev codes. Each method returns <see langword="false"/> when the sink drops the event.
/// </summary>
public interface IInputSink
{
    /// <summary>Creates a keyboard device that can carry its own keymap.</summary>
    /// <returns>The new device, or <see langword="null"/> when the sink cannot create one. The caller owns it and must dispose it.</returns>
    IInjectedKeyboard? CreateKeyboard();

    /// <summary>Sends a key press or release through a keyboard device.</summary>
    /// <param name="keyboard">The device whose keymap applies, or <see langword="null"/> for the seat keymap.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool Key(IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed);

    /// <summary>Sets the XKB modifier state of a keyboard device.</summary>
    /// <param name="keyboard">The device whose keymap applies, or <see langword="null"/> for the seat keymap.</param>
    /// <param name="depressed">The mask of modifiers that are held down.</param>
    /// <param name="latched">The mask of latched modifiers.</param>
    /// <param name="locked">The mask of locked modifiers.</param>
    /// <param name="group">The effective layout index.</param>
    /// <returns><see langword="true"/> when the sink accepted the state.</returns>
    bool Modifiers(IInjectedKeyboard? keyboard, uint depressed, uint latched, uint locked, uint group);

    /// <summary>Moves the pointer by a relative distance in layout coordinates.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The horizontal distance.</param>
    /// <param name="dy">The vertical distance.</param>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool PointerMotion(uint timeMs, double dx, double dy);

    /// <summary>
    /// Moves the pointer to a point in an extent of <paramref name="width"/> by <paramref name="height"/>.
    /// An empty extent gives <see langword="false"/>.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in the extent.</param>
    /// <param name="y">The vertical position in the extent.</param>
    /// <param name="width">The width of the extent.</param>
    /// <param name="height">The height of the extent.</param>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height);

    /// <summary>Presses or releases a pointer button.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The evdev button code, for example <c>BTN_LEFT</c>.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool PointerButton(uint timeMs, uint button, bool pressed);

    /// <summary>Scrolls on one axis. The source from the last <see cref="PointerAxisSource"/> call applies.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The <c>wl_pointer.axis</c> value: 0 is vertical, 1 is horizontal.</param>
    /// <param name="value">The scroll distance in the units of <c>wl_pointer.axis</c>.</param>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool PointerAxis(uint timeMs, uint axis, double value);

    /// <summary>Sets the source of the scroll events that follow.</summary>
    /// <param name="source">The <c>wl_pointer.axis_source</c> value, for example 0 for a wheel.</param>
    /// <returns><see langword="true"/> when the sink accepted the source.</returns>
    bool PointerAxisSource(uint source);

    /// <summary>Ends a scroll sequence on one axis, for example when the fingers leave a touchpad.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The <c>wl_pointer.axis</c> value that stops.</param>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool PointerAxisStop(uint timeMs, uint axis);

    /// <summary>Ends a group of pointer events that belong together.</summary>
    /// <returns><see langword="true"/> when the sink accepted the frame.</returns>
    bool Frame();

    /// <summary>Starts a touch point in an extent of <paramref name="width"/> by <paramref name="height"/>. The default implementation returns <see langword="false"/>.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The identifier of the touch point.</param>
    /// <param name="x">The horizontal position in the extent.</param>
    /// <param name="y">The vertical position in the extent.</param>
    /// <param name="width">The width of the extent.</param>
    /// <param name="height">The height of the extent.</param>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool TouchDown(uint timeMs, int id, double x, double y, double width, double height) => false;

    /// <summary>Moves a touch point. The default implementation returns <see langword="false"/>.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The identifier of the touch point.</param>
    /// <param name="x">The horizontal position in the extent.</param>
    /// <param name="y">The vertical position in the extent.</param>
    /// <param name="width">The width of the extent.</param>
    /// <param name="height">The height of the extent.</param>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool TouchMotion(uint timeMs, int id, double x, double y, double width, double height) => false;

    /// <summary>Ends a touch point. The default implementation returns <see langword="false"/>.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The identifier of the touch point.</param>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool TouchUp(uint timeMs, int id) => false;

    /// <summary>Ends a group of touch events that belong together. The default implementation returns <see langword="false"/>.</summary>
    /// <returns><see langword="true"/> when the sink accepted the frame.</returns>
    bool TouchFrame() => false;

    /// <summary>Cancels all active touch points. The default implementation returns <see langword="false"/>.</summary>
    /// <returns><see langword="true"/> when the sink accepted the event.</returns>
    bool TouchCancel() => false;

    /// <summary>Tells which kinds of input the sink can deliver. The default implementation gives keyboard and pointer.</summary>
    /// <returns>The supported capabilities.</returns>
    InputDeviceCapability Supports() => InputDeviceCapability.Keyboard | InputDeviceCapability.Pointer;
}
