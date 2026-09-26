using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcCaptureOutputParams : IIpcParams, IIpcReusable
{
    private IpcCaptureTo _to;

    private int _present;

    public IpcCaptureOutputParams()
    {
    }

    public IpcCaptureOutputParams(
        IpcCaptureTo to,
        string? output = null,
        bool? cursor = null,
        double? scale = null,
        long? maxDimension = null)
    {
        To = to;
        Output = output;
        Cursor = cursor;
        Scale = scale;
        MaxDimension = maxDimension;
    }

    public IpcCaptureTo To
    {
        get => _to;
        set
        {
            _to = value;
            _present |= 1;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Output { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Cursor { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Scale { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxDimension { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'to' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _to = default;
        _present = 0;
        Output = null;
        Cursor = null;
        Scale = null;
        MaxDimension = null;
    }
}
