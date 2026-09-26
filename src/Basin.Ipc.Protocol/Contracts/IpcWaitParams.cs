using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcWaitParams : IIpcParams, IIpcReusable
{
    public IpcWaitParams()
    {
    }

    public IpcWaitParams(string? appId = null, string? title = null, long? timeoutMs = null)
    {
        AppId = appId;
        Title = title;
        TimeoutMs = timeoutMs;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TimeoutMs { get; set; }

    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        AppId = null;
        Title = null;
        TimeoutMs = null;
    }
}
