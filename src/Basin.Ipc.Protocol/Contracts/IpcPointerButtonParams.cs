using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcPointerButtonParams : IIpcParams, IIpcReusable
{
    private IpcButton _button;

    private int _present;

    public IpcPointerButtonParams()
    {
    }

    public IpcPointerButtonParams(IpcButton button, bool? pressed = null)
    {
        Button = button;
        Pressed = pressed;
    }

    public IpcButton Button
    {
        get => _button;
        set
        {
            _button = value;
            _present |= 1;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Pressed { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'button' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _button = default;
        _present = 0;
        Pressed = null;
    }
}
