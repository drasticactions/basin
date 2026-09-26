using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcOutputChange : IIpcParams, IIpcReusable
{
    private string _name = string.Empty;

    private int _present;

    public IpcOutputChange()
    {
    }

    public IpcOutputChange(
        string name,
        bool? enabled = null,
        IpcModeChange? mode = null,
        IpcPoint? position = null,
        double? scale = null,
        string? transform = null,
        bool? adaptiveSync = null)
    {
        Name = name;
        Enabled = enabled;
        Mode = mode;
        Position = position;
        Scale = scale;
        Transform = transform;
        AdaptiveSync = adaptiveSync;
    }

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _present |= value is null ? 0 : 1;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Enabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IpcModeChange? Mode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IpcPoint? Position { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Scale { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Transform { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AdaptiveSync { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'name' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _name = string.Empty;
        _present = 0;
        Enabled = null;
        Mode = null;
        Position = null;
        Scale = null;
        Transform = null;
        AdaptiveSync = null;
    }
}
