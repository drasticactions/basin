using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcClipboardParams : IIpcParams, IIpcReusable
{
    public IpcClipboardParams()
    {
    }

    public IpcClipboardParams(string? kind = null, string? mime = null, long? timeoutMs = null, long? maxBytes = null)
    {
        Kind = kind;
        Mime = mime;
        TimeoutMs = timeoutMs;
        MaxBytes = maxBytes;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mime { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TimeoutMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxBytes { get; set; }

    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Kind = null;
        Mime = null;
        TimeoutMs = null;
        MaxBytes = null;
    }
}
