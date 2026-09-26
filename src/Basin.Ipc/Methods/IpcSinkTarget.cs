using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcSinkTarget(IInputSink sink, OutputLayout? layout) : IIpcInputTarget
{
    private static readonly object KeyboardKey = new();

    public bool Move(uint timeMs, double x, double y)
    {
        if (layout is null || layout.Bounds is not { Width: > 0, Height: > 0 } bounds)
        {
            return false;
        }

        return sink.PointerMotionAbsolute(timeMs, x - bounds.X, y - bounds.Y, bounds.Width, bounds.Height) && sink.Frame();
    }

    public bool Button(uint timeMs, uint button, bool pressed) =>
        sink.PointerButton(timeMs, button, pressed) && sink.Frame();

    public bool Axis(uint timeMs, uint axis, double value, uint source) =>
        sink.PointerAxisSource(source) && sink.PointerAxis(timeMs, axis, value) && sink.Frame();

    public bool Key(IpcReply reply, uint timeMs, uint keycode, bool pressed)
    {
        var keyboard = reply.State.Get<IInjectedKeyboard>(KeyboardKey);
        if (keyboard is null && sink.CreateKeyboard() is { } created)
        {
            reply.State.Set(KeyboardKey, created);
            keyboard = created;
        }

        return sink.Key(keyboard, timeMs, keycode, pressed);
    }

    public bool Touch(uint timeMs, string kind, int id, double x, double y)
    {
        if (layout is null || layout.Bounds is not { Width: > 0, Height: > 0 } bounds)
        {
            return false;
        }

        return kind switch
        {
            "down" => sink.TouchDown(timeMs, id, x - bounds.X, y - bounds.Y, bounds.Width, bounds.Height),
            "motion" => sink.TouchMotion(timeMs, id, x - bounds.X, y - bounds.Y, bounds.Width, bounds.Height),
            "up" => sink.TouchUp(timeMs, id),
            "frame" => sink.TouchFrame(),
            _ => sink.TouchCancel(),
        };
    }
}
