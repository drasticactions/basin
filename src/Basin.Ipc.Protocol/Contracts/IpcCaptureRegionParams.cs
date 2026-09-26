using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcCaptureRegionParams : IIpcParams, IIpcReusable
{
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private IpcCaptureTo _to;

    private int _present;

    public IpcCaptureRegionParams()
    {
    }

    public IpcCaptureRegionParams(
        int x,
        int y,
        int width,
        int height,
        IpcCaptureTo to,
        bool? cursor = null,
        double? scale = null,
        long? maxDimension = null)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        To = to;
        Cursor = cursor;
        Scale = scale;
        MaxDimension = maxDimension;
    }

    public int X
    {
        get => _x;
        set
        {
            _x = value;
            _present |= 1;
        }
    }

    public int Y
    {
        get => _y;
        set
        {
            _y = value;
            _present |= 2;
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            _width = value;
            _present |= 4;
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            _height = value;
            _present |= 8;
        }
    }

    public IpcCaptureTo To
    {
        get => _to;
        set
        {
            _to = value;
            _present |= 16;
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Cursor { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Scale { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxDimension { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'x' is required"
        : (_present & 2) == 0 ? "'y' is required"
        : (_present & 4) == 0 ? "'width' is required"
        : (_present & 8) == 0 ? "'height' is required"
        : (_present & 16) == 0 ? "'to' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _x = default;
        _y = default;
        _width = default;
        _height = default;
        _to = default;
        _present = 0;
        Cursor = null;
        Scale = null;
        MaxDimension = null;
    }
}
