using Basin.Capabilities;

namespace Basin.Seat.Backends;

/// <summary>
/// Is a <see cref="SeatInputSink"/> whose input methods call a hook when one is set. A hook result replaces the seat delivery.
/// </summary>
public sealed class HookInputSink : SeatInputSink
{
    /// <summary>
    /// Gets or sets the hook for <see cref="Key"/>. It takes the keyboard, the time, the keycode and the press state, and returns the result.
    /// </summary>
    public Func<IInjectedKeyboard?, uint, uint, bool, bool>? OnKey { get; set; }

    /// <summary>
    /// Gets or sets the hook for <see cref="Modifiers"/>. It takes the keyboard and the four xkb masks, and returns the result.
    /// </summary>
    public Func<IInjectedKeyboard?, uint, uint, uint, uint, bool>? OnModifiers { get; set; }

    /// <summary>
    /// Gets or sets the hook for <see cref="PointerMotion"/>. It takes the time and the relative motion, and returns the result.
    /// </summary>
    public Func<uint, double, double, bool>? OnPointerMotion { get; set; }

    /// <summary>
    /// Gets or sets the hook for <see cref="PointerMotionAbsolute"/>. It takes the time, the position and the extent, and returns the result.
    /// </summary>
    public Func<uint, double, double, double, double, bool>? OnPointerMotionAbsolute { get; set; }

    /// <summary>
    /// Gets or sets the hook for <see cref="PointerButton"/>. It takes the time, the button and the press state, and returns the result.
    /// </summary>
    public Func<uint, uint, bool, bool>? OnPointerButton { get; set; }

    /// <summary>
    /// Gets or sets the hook for <see cref="PointerAxis"/>. It takes the time, the axis, the value and the last axis source, and returns the result.
    /// </summary>
    public Func<uint, uint, double, uint, bool>? OnPointerAxis { get; set; }

    /// <summary>
    /// Gets or sets the callback that runs before <see cref="CreateKeyboard"/> creates a keyboard.
    /// </summary>
    public Action? OnKeyboardCreated { get; set; }

    private uint _axisSource;

    /// <summary>
    /// Sets the hooks so that keys and pointer events go through a <see cref="SeatInjector"/>. Each key also makes its keyboard the active seat keyboard.
    /// </summary>
    /// <param name="injector">The injector that delivers the pointer events and, without <paramref name="key"/>, the keys.</param>
    /// <param name="seat">The seat whose keyboard is activated and whose keyboard capability is set on keyboard creation.</param>
    /// <param name="key">An optional key handler that replaces the injector's key path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="injector"/> or <paramref name="seat"/> is <see langword="null"/>.</exception>
    public void Route(SeatInjector injector, Seat seat, Action<IInjectedKeyboard?, uint, uint, bool>? key = null)
    {
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(seat);
        OnKey = (keyboard, timeMs, keycode, pressed) =>
        {
            seat.Keyboard.Activate(keyboard);
            if (key is null)
            {
                return injector.Key(timeMs, keycode, pressed);
            }

            key(keyboard, timeMs, keycode, pressed);
            return true;
        };
        OnPointerAxis = injector.DeliverAxis is null ? null : injector.PointerAxis;
        OnKeyboardCreated = () => seat.SetCapability(SeatCapability.Keyboard, true);
        OnPointerMotion = injector.PointerMotion;
        OnPointerMotionAbsolute = injector.MotionAbsolute;
        OnPointerButton = injector.PointerButton;
    }

    /// <summary>
    /// Calls <see cref="OnKeyboardCreated"/>, then creates the keyboard as the base sink does.
    /// </summary>
    /// <returns>The new keyboard device, or <see langword="null"/> when the sink cannot create one.</returns>
    public override IInjectedKeyboard? CreateKeyboard()
    {
        OnKeyboardCreated?.Invoke();
        return base.CreateKeyboard();
    }

    /// <summary>
    /// Calls <see cref="OnKey"/> when it is set. Otherwise the base sink delivers the key.
    /// </summary>
    /// <param name="keyboard">The device whose keymap applies, or <see langword="null"/> for the seat keymap.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns>The hook result, or the base result.</returns>
    public override bool Key(IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed) =>
        OnKey?.Invoke(keyboard, timeMs, keycode, pressed) ?? base.Key(keyboard, timeMs, keycode, pressed);

    /// <summary>
    /// Calls <see cref="OnModifiers"/> when it is set. Otherwise the base sink sets the state.
    /// </summary>
    /// <param name="keyboard">The device whose keymap applies, or <see langword="null"/> for the seat keymap.</param>
    /// <param name="depressed">The depressed modifier mask.</param>
    /// <param name="latched">The latched modifier mask.</param>
    /// <param name="locked">The locked modifier mask.</param>
    /// <param name="group">The effective layout group.</param>
    /// <returns>The hook result, or the base result.</returns>
    public override bool Modifiers(IInjectedKeyboard? keyboard, uint depressed, uint latched, uint locked, uint group) =>
        OnModifiers?.Invoke(keyboard, depressed, latched, locked, group)
            ?? base.Modifiers(keyboard, depressed, latched, locked, group);

    /// <summary>
    /// Calls <see cref="OnPointerMotion"/> when it is set. Otherwise the base sink moves the pointer.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The relative x motion.</param>
    /// <param name="dy">The relative y motion.</param>
    /// <returns>The hook result, or the base result.</returns>
    public override bool PointerMotion(uint timeMs, double dx, double dy) =>
        OnPointerMotion?.Invoke(timeMs, dx, dy) ?? base.PointerMotion(timeMs, dx, dy);

    /// <summary>
    /// Calls <see cref="OnPointerMotionAbsolute"/> when it is set. Otherwise the base sink moves the pointer.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The x position in the extent.</param>
    /// <param name="y">The y position in the extent.</param>
    /// <param name="width">The extent width.</param>
    /// <param name="height">The extent height.</param>
    /// <returns>The hook result, or the base result.</returns>
    public override bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height) =>
        OnPointerMotionAbsolute?.Invoke(timeMs, x, y, width, height)
            ?? base.PointerMotionAbsolute(timeMs, x, y, width, height);

    /// <summary>
    /// Keeps the source for <see cref="OnPointerAxis"/> and passes it to the base sink.
    /// </summary>
    /// <param name="source">The wl_pointer.axis_source value.</param>
    /// <returns>The base result.</returns>
    public override bool PointerAxisSource(uint source)
    {
        _axisSource = source;
        return base.PointerAxisSource(source);
    }

    /// <summary>
    /// Calls <see cref="OnPointerAxis"/> with the last axis source when it is set. Otherwise the base sink scrolls.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The wl_pointer.axis value.</param>
    /// <param name="value">The scroll distance.</param>
    /// <returns>The hook result, or the base result.</returns>
    public override bool PointerAxis(uint timeMs, uint axis, double value) =>
        OnPointerAxis?.Invoke(timeMs, axis, value, _axisSource) ?? base.PointerAxis(timeMs, axis, value);

    /// <summary>
    /// Calls <see cref="OnPointerButton"/> when it is set. Otherwise the base sink delivers the button.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The evdev button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns>The hook result, or the base result.</returns>
    public override bool PointerButton(uint timeMs, uint button, bool pressed) =>
        OnPointerButton?.Invoke(timeMs, button, pressed) ?? base.PointerButton(timeMs, button, pressed);
}
