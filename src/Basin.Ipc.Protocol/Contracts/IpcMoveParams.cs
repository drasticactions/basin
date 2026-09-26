using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcMoveParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private int _x;
    private int _y;

    private int _present;

    public IpcMoveParams()
    {
    }

    public IpcMoveParams(ulong id, int x, int y)
    {
        Id = id;
        X = x;
        Y = y;
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

    public int X
    {
        get => _x;
        set
        {
            _x = value;
            _present |= 2;
        }
    }

    public int Y
    {
        get => _y;
        set
        {
            _y = value;
            _present |= 4;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'x' is required"
        : (_present & 4) == 0 ? "'y' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _x = default;
        _y = default;
        _present = 0;
    }
}
