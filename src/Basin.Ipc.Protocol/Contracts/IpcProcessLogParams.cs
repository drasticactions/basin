using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcProcessLogParams : IIpcParams, IIpcReusable
{
    private long _launchId;

    private int _present;

    public IpcProcessLogParams()
    {
    }

    public IpcProcessLogParams(long launchId, long? lines = null)
    {
        LaunchId = launchId;
        Lines = lines;
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
    public long? Lines { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'launch_id' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _launchId = default;
        _present = 0;
        Lines = null;
    }
}
