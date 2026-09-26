using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcResizeParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private int _width;
    private int _height;

    private int _present;

    public IpcResizeParams()
    {
    }

    public IpcResizeParams(ulong id, int width, int height)
    {
        Id = id;
        Width = width;
        Height = height;
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

    public int Width
    {
        get => _width;
        set
        {
            _width = value;
            _present |= 2;
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            _height = value;
            _present |= 4;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'width' is required"
        : (_present & 4) == 0 ? "'height' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _width = default;
        _height = default;
        _present = 0;
    }
}
