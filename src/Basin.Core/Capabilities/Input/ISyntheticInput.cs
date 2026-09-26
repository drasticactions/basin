namespace Basin.Capabilities;

public interface ISyntheticInput
{
    bool PointerMotionAbsolute(uint timeMs, double x, double y);

    bool PointerButton(uint timeMs, uint button, bool pressed);

    bool PointerAxis(uint timeMs, uint axis, double value, uint source) => false;

    bool Key(uint timeMs, uint keycode, bool pressed);

    bool TouchDown(uint timeMs, int id, double x, double y) => false;

    bool TouchMotion(uint timeMs, int id, double x, double y) => false;

    bool TouchUp(uint timeMs, int id) => false;

    bool TouchFrame() => false;

    bool TouchCancel() => false;

    bool TryPointerPosition(out double x, out double y)
    {
        x = 0;
        y = 0;
        return false;
    }
}
