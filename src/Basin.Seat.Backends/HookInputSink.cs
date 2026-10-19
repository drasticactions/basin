using Basin.Capabilities;

namespace Basin.Seat.Backends;

public sealed class HookInputSink : SeatInputSink
{
    public Func<IInjectedKeyboard?, uint, uint, bool, bool>? OnKey { get; set; }

    public Func<IInjectedKeyboard?, uint, uint, uint, uint, bool>? OnModifiers { get; set; }

    public Func<uint, double, double, bool>? OnPointerMotion { get; set; }

    public Func<uint, double, double, double, double, bool>? OnPointerMotionAbsolute { get; set; }

    public Func<uint, uint, bool, bool>? OnPointerButton { get; set; }

    public Func<uint, uint, double, uint, bool>? OnPointerAxis { get; set; }

    public Action? OnKeyboardCreated { get; set; }

    private uint _axisSource;

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

    public override IInjectedKeyboard? CreateKeyboard()
    {
        OnKeyboardCreated?.Invoke();
        return base.CreateKeyboard();
    }

    public override bool Key(IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed) =>
        OnKey?.Invoke(keyboard, timeMs, keycode, pressed) ?? base.Key(keyboard, timeMs, keycode, pressed);

    public override bool Modifiers(IInjectedKeyboard? keyboard, uint depressed, uint latched, uint locked, uint group) =>
        OnModifiers?.Invoke(keyboard, depressed, latched, locked, group)
            ?? base.Modifiers(keyboard, depressed, latched, locked, group);

    public override bool PointerMotion(uint timeMs, double dx, double dy) =>
        OnPointerMotion?.Invoke(timeMs, dx, dy) ?? base.PointerMotion(timeMs, dx, dy);

    public override bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height) =>
        OnPointerMotionAbsolute?.Invoke(timeMs, x, y, width, height)
            ?? base.PointerMotionAbsolute(timeMs, x, y, width, height);

    public override bool PointerAxisSource(uint source)
    {
        _axisSource = source;
        return base.PointerAxisSource(source);
    }

    public override bool PointerAxis(uint timeMs, uint axis, double value) =>
        OnPointerAxis?.Invoke(timeMs, axis, value, _axisSource) ?? base.PointerAxis(timeMs, axis, value);

    public override bool PointerButton(uint timeMs, uint button, bool pressed) =>
        OnPointerButton?.Invoke(timeMs, button, pressed) ?? base.PointerButton(timeMs, button, pressed);
}
