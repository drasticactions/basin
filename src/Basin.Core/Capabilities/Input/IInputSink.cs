namespace Basin.Capabilities;

public interface IInputSink
{
    IInjectedKeyboard? CreateKeyboard();

    bool Key(IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed);

    bool Modifiers(IInjectedKeyboard? keyboard, uint depressed, uint latched, uint locked, uint group);

    bool PointerMotion(uint timeMs, double dx, double dy);

    bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height);

    bool PointerButton(uint timeMs, uint button, bool pressed);

    bool PointerAxis(uint timeMs, uint axis, double value);

    bool PointerAxisSource(uint source);

    bool PointerAxisStop(uint timeMs, uint axis);

    bool Frame();
}
