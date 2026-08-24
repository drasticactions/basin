using Basin.Capabilities;

namespace Basin.Seat.Backends;

public sealed class HookInputSink : SeatInputSink
{
    public Func<IInjectedKeyboard?, uint, uint, bool, bool>? OnKey { get; set; }

    public Func<IInjectedKeyboard?, uint, uint, uint, uint, bool>? OnModifiers { get; set; }

    public Func<uint, double, double, bool>? OnPointerMotion { get; set; }

    public Func<uint, double, double, double, double, bool>? OnPointerMotionAbsolute { get; set; }

    public Func<uint, uint, bool, bool>? OnPointerButton { get; set; }

    public Action? OnKeyboardCreated { get; set; }

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

    public override bool PointerButton(uint timeMs, uint button, bool pressed) =>
        OnPointerButton?.Invoke(timeMs, button, pressed) ?? base.PointerButton(timeMs, button, pressed);
}
