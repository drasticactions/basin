using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcFrameLimitParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private int _fps;

    private int _present;

    public IpcFrameLimitParams()
    {
    }

    public IpcFrameLimitParams(ulong id, int fps)
    {
        Id = id;
        Fps = fps;
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

    public int Fps
    {
        get => _fps;
        set
        {
            _fps = value;
            _present |= 2;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'fps' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _fps = default;
        _present = 0;
    }
}
