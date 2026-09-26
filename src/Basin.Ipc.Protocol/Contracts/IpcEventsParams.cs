using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcEventsParams : IIpcParams, IIpcReusable
{
    public IpcEventsParams()
    {
    }

    public IpcEventsParams(IReadOnlyList<string>? events = null)
    {
        Events = events;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Events { get; set; }

    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Events = null;
    }
}
