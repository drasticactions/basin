namespace TinyComp;

internal sealed class TinyCompInputSink : Basin.Seat.SeatInputSink
{
    private readonly TinyComp _comp;

    public TinyCompInputSink(TinyComp comp) => _comp = comp;

    public override bool Key(Basin.Capabilities.IInjectedKeyboard? keyboard, uint timeMs, uint keycode, bool pressed)
    {
        Seat.Keyboard.Activate(keyboard);
        _comp.InjectKey(timeMs, keycode, pressed);
        return true;
    }

    public override bool PointerMotion(uint timeMs, double dx, double dy)
    {
        _comp.InjectPointerMotion(timeMs, dx, dy);
        return true;
    }

    public override bool PointerMotionAbsolute(uint timeMs, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        _comp.InjectPointerMotionAbsolute(timeMs, x / width, y / height);
        return true;
    }

    public override bool PointerButton(uint timeMs, uint button, bool pressed)
    {
        _comp.InjectPointerButton(timeMs, button, pressed);
        return true;
    }
}
