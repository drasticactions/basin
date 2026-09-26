using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcOutputsParams : IIpcParams, IIpcReusable
{
    private IReadOnlyList<IpcOutputChange> _entries = [];

    private int _present;

    public IpcOutputsParams()
    {
    }

    public IpcOutputsParams(IReadOnlyList<IpcOutputChange> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<IpcOutputChange> Entries
    {
        get => _entries;
        set
        {
            _entries = value;
            _present |= value is null ? 0 : 1;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'entries' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _entries = [];
        _present = 0;
    }
}
