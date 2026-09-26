using Basin.Capabilities;
using Basin.Hosted;

namespace Basin.Shell.Nested;

public sealed class NestedSyntheticInput : ISyntheticInput
{
    private const double AxisStep = 10;

    private readonly NestedShell _shell;

    public NestedSyntheticInput(NestedShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    public bool PointerMotionAbsolute(uint timeMs, double x, double y) =>
        Send(new BasinViewInput(BasinViewInputKind.PointerMotion, timeMs, x, y, 0, false, 0, 0, 0));

    public bool PointerButton(uint timeMs, uint button, bool pressed) =>
        Send(new BasinViewInput(BasinViewInputKind.PointerButton, timeMs, 0, 0, button, pressed, 0, 0, 0));

    public bool PointerAxis(uint timeMs, uint axis, double value, uint source) =>
        axis switch
        {
            0 => Send(new BasinViewInput(BasinViewInputKind.PointerAxis, timeMs, 0, 0, 0, false, 0, -value / AxisStep, 0)),
            1 => Send(new BasinViewInput(BasinViewInputKind.PointerAxis, timeMs, 0, 0, 0, false, -value / AxisStep, 0, 0)),
            _ => false,
        };

    public bool Key(uint timeMs, uint keycode, bool pressed) =>
        Send(new BasinViewInput(BasinViewInputKind.Key, timeMs, 0, 0, keycode, pressed, 0, 0, 0));

    public bool TouchDown(uint timeMs, int id, double x, double y) =>
        Send(new BasinViewInput(BasinViewInputKind.TouchDown, timeMs, x, y, 0, false, 0, 0, id));

    public bool TouchMotion(uint timeMs, int id, double x, double y) =>
        Send(new BasinViewInput(BasinViewInputKind.TouchMotion, timeMs, x, y, 0, false, 0, 0, id));

    public bool TouchUp(uint timeMs, int id) =>
        Send(new BasinViewInput(BasinViewInputKind.TouchUp, timeMs, 0, 0, 0, false, 0, 0, id));

    public bool TouchFrame() => !_shell.IsDisposed;

    public bool TouchCancel() =>
        Send(new BasinViewInput(BasinViewInputKind.TouchCancel, 0, 0, 0, 0, false, 0, 0, 0));

    public bool TryPointerPosition(out double x, out double y)
    {
        (x, y) = _shell.PointerPosition;
        return !_shell.IsDisposed;
    }

    private bool Send(in BasinViewInput input)
    {
        if (_shell.IsDisposed)
        {
            return false;
        }

        _shell.HandleInput(input);
        return true;
    }
}
