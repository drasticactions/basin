using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcIdParams : IIpcParams, IIpcReusable
{
    private ulong _id;

    private int _present;

    public IpcIdParams()
    {
    }

    public IpcIdParams(ulong id)
    {
        Id = id;
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

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _present = 0;
    }
}
