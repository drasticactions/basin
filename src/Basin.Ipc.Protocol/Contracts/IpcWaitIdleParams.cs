using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcWaitIdleParams : IIpcParams, IIpcReusable
{
    public IpcWaitIdleParams()
    {
    }

    public IpcWaitIdleParams(ulong? id = null, long? quietMs = null, long? ignoreBelow = null, long? timeoutMs = null)
    {
        Id = id;
        QuietMs = quietMs;
        IgnoreBelow = ignoreBelow;
        TimeoutMs = timeoutMs;
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? QuietMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? IgnoreBelow { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TimeoutMs { get; set; }

    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Id = null;
        QuietMs = null;
        IgnoreBelow = null;
        TimeoutMs = null;
    }
}
