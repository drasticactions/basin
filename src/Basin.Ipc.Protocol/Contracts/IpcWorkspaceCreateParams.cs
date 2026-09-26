using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcWorkspaceCreateParams : IIpcParams, IIpcReusable
{
    private ulong _group;
    private string _name = string.Empty;

    private int _present;

    public IpcWorkspaceCreateParams()
    {
    }

    public IpcWorkspaceCreateParams(ulong group, string name)
    {
        Group = group;
        Name = name;
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Group
    {
        get => _group;
        set
        {
            _group = value;
            _present |= 1;
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _present |= value is null ? 0 : 2;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'group' is required"
        : (_present & 2) == 0 ? "'name' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _group = default;
        _name = string.Empty;
        _present = 0;
    }
}
