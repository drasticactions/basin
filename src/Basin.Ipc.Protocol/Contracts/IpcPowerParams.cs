using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcPowerParams : IIpcParams, IIpcReusable
{
    private string _output = string.Empty;
    private bool _on;

    private int _present;

    public IpcPowerParams()
    {
    }

    public IpcPowerParams(string output, bool on)
    {
        Output = output;
        On = on;
    }

    public string Output
    {
        get => _output;
        set
        {
            _output = value;
            _present |= value is null ? 0 : 1;
        }
    }

    public bool On
    {
        get => _on;
        set
        {
            _on = value;
            _present |= 2;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'output' is required"
        : (_present & 2) == 0 ? "'on' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _output = string.Empty;
        _on = default;
        _present = 0;
    }
}
