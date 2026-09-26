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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? X { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Y { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Window { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Raise { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'button' is required"
        : null;

    void IIpcReusable.Reset()
    {
        Window = null;
        Raise = null;
        X = null;
        Y = null;
        _button = default;
        _present = 0;
        Pressed = null;
    }
}
