using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcSendToWorkspaceParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private ulong _workspace;

    private int _present;

    public IpcSendToWorkspaceParams()
    {
    }

    public IpcSendToWorkspaceParams(ulong id, ulong workspace)
    {
        Id = id;
        Workspace = workspace;
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

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Workspace
    {
        get => _workspace;
        set
        {
            _workspace = value;
            _present |= 2;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'workspace' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _workspace = default;
        _present = 0;
    }
}
