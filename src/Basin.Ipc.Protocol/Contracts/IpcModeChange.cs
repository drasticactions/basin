using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcModeChange : IIpcParams, IIpcReusable
{
    private int _width;
    private int _height;

    private int _present;

    public IpcModeChange()
    {
    }

    public IpcModeChange(int width, int height, int? refreshMhz = null)
    {
        Width = width;
        Height = height;
        RefreshMhz = refreshMhz;
    }

    public int Width
    {
        get => _width;
        set
        {
            _width = value;
            _present |= 1;
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            _height = value;
            _present |= 2;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RefreshMhz { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'width' is required"
        : (_present & 2) == 0 ? "'height' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _width = default;
        _height = default;
        _present = 0;
        RefreshMhz = null;
    }
}
