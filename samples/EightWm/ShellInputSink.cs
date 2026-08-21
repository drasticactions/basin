using Basin;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Basin.Host;
using Basin.Scene;
using Basin.Seat;
using Basin.Seat.Backends;
using Microsoft.Extensions.Logging;
using Xkb;

namespace EightWm;

internal sealed class ShellInputSink : SeatInputSink
{
    public Func<IInjectedKeyboard?, uint, uint, bool, bool>? OnKey { get; set; }

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

    public override bool PointerMotion(uint timeMs, double dx, double dy) =>
        OnPointerMotion?.Invoke(timeMs, dx, dy) ?? base.PointerMotion(timeMs, dx, dy);

    public override bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height) =>
        OnPointerMotionAbsolute?.Invoke(timeMs, x, y, width, height)
            ?? base.PointerMotionAbsolute(timeMs, x, y, width, height);

    public override bool PointerButton(uint timeMs, uint button, bool pressed) =>
        OnPointerButton?.Invoke(timeMs, button, pressed) ?? base.PointerButton(timeMs, button, pressed);
}
