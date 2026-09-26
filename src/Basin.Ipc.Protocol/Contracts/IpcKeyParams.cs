using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcKeyParams : IIpcParams, IIpcReusable
{
    private int _code;

    private int _present;

    public IpcKeyParams()
    {
    }

    public IpcKeyParams(int code, bool? pressed = null)
    {
        Code = code;
        Pressed = pressed;
    }

    public int Code
    {
        get => _code;
        set
        {
            _code = value;
            _present |= 1;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Pressed { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'code' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _code = default;
        _present = 0;
        Pressed = null;
    }
}
