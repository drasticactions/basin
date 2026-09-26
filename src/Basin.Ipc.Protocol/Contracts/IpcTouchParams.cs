using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcTouchParams : IIpcParams, IIpcReusable
{
    private string _kind = string.Empty;

    private int _present;

    public IpcTouchParams()
    {
    }

    public IpcTouchParams(string kind, int? id = null, double? x = null, double? y = null)
    {
        Kind = kind;
        Id = id;
        X = x;
        Y = y;
    }

    public string Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            _present |= value is null ? 0 : 1;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? X { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Y { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'kind' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _kind = string.Empty;
        _present = 0;
        Id = null;
        X = null;
        Y = null;
    }
}
