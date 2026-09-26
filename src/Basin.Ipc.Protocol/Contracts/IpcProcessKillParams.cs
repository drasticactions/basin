using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcProcessKillParams : IIpcParams, IIpcReusable
{
    private long _launchId;

    private int _present;

    public IpcProcessKillParams()
    {
    }

    public IpcProcessKillParams(long launchId, long? graceMs = null)
    {
        LaunchId = launchId;
        GraceMs = graceMs;
    }

    public long LaunchId
    {
        get => _launchId;
        set
        {
            _launchId = value;
            _present |= 1;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? GraceMs { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'launch_id' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _launchId = default;
        _present = 0;
        GraceMs = null;
    }
}
