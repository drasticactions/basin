using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcCaptureWindowParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private IpcCaptureTo _to;

    private int _present;

    public IpcCaptureWindowParams()
    {
    }

    public IpcCaptureWindowParams(
        ulong id,
        IpcCaptureTo to,
        bool? clientOnly = null,
        bool? cursor = null,
        double? scale = null,
        long? maxDimension = null)
    {
        Id = id;
        To = to;
        ClientOnly = clientOnly;
        Cursor = cursor;
        Scale = scale;
        MaxDimension = maxDimension;
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Id
    {
        get => _id;
        set
        {
            _id = value;
            _present |= 1;
        }
    }

    public IpcCaptureTo To
    {
        get => _to;
        set
        {
            _to = value;
            _present |= 2;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ClientOnly { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Cursor { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Scale { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxDimension { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'to' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _to = default;
        _present = 0;
        ClientOnly = null;
        Cursor = null;
        Scale = null;
        MaxDimension = null;
    }
}
