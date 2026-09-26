using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcPointerMoveParams : IIpcParams, IIpcReusable
{
    private double _x;
    private double _y;

    private int _present;

    public IpcPointerMoveParams()
    {
    }

    public IpcPointerMoveParams(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X
    {
        get => _x;
        set
        {
            _x = value;
            _present |= 1;
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            _y = value;
            _present |= 2;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'x' is required"
        : (_present & 2) == 0 ? "'y' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _x = default;
        _y = default;
        _present = 0;
    }
}
