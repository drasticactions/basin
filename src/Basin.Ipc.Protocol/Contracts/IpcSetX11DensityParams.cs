using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcSetX11DensityParams : IIpcParams, IIpcReusable
{
    private ulong _id;

    private string _density = string.Empty;

    private int _present;

    public IpcSetX11DensityParams()
    {
    }

    public IpcSetX11DensityParams(ulong id, string density)
    {
        Id = id;
        Density = density;
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

    public string Density
    {
        get => _density;
        set
        {
            _density = value;
            _present |= value is null ? 0 : 2;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'density' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _density = string.Empty;
        _present = 0;
    }
}
