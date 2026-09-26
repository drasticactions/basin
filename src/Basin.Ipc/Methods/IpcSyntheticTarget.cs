using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcSyntheticTarget(ISyntheticInput input) : IIpcInputTarget
{
    public bool Move(uint timeMs, double x, double y) => input.PointerMotionAbsolute(timeMs, x, y);

    public bool Button(uint timeMs, uint button, bool pressed) => input.PointerButton(timeMs, button, pressed);

    public bool Axis(uint timeMs, uint axis, double value, uint source) => input.PointerAxis(timeMs, axis, value, source);

    public bool Key(IpcReply reply, uint timeMs, uint keycode, bool pressed) => input.Key(timeMs, keycode, pressed);

    public bool Touch(uint timeMs, string kind, int id, double x, double y) => kind switch
    {
        "down" => input.TouchDown(timeMs, id, x, y),
        "motion" => input.TouchMotion(timeMs, id, x, y),
        "up" => input.TouchUp(timeMs, id),
        "frame" => input.TouchFrame(),
        _ => input.TouchCancel(),
    };
}
