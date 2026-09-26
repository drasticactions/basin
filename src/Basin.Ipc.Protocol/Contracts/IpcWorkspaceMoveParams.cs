using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcWorkspaceMoveParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private ulong _group;

    private int _present;

    public IpcWorkspaceMoveParams()
    {
    }

    public IpcWorkspaceMoveParams(ulong id, ulong group)
    {
        Id = id;
        Group = group;
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
    public ulong Group
    {
        get => _group;
        set
        {
            _group = value;
            _present |= 2;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'group' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _group = default;
        _present = 0;
    }
}
