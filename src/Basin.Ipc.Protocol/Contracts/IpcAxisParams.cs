using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcAxisParams : IIpcParams, IIpcReusable
{
    private double _value;

    private int _present;

    public IpcAxisParams()
    {
    }

    public IpcAxisParams(double value, string? axis = null, string? source = null)
    {
        Value = value;
        Axis = axis;
        Source = source;
    }

    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            _present |= 1;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Axis { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'value' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _value = default;
        _present = 0;
        Axis = null;
        Source = null;
    }
}
