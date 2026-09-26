namespace Basin.Ipc;

internal interface IIpcInputTarget
{
    bool Move(uint timeMs, double x, double y);

    bool Button(uint timeMs, uint button, bool pressed);

    bool Axis(uint timeMs, uint axis, double value, uint source);

    bool Key(IpcReply reply, uint timeMs, uint keycode, bool pressed);

    bool Touch(uint timeMs, string kind, int id, double x, double y);
}
