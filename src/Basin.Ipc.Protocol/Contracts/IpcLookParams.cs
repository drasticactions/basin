using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcLookParams : IIpcParams, IIpcReusable
{
    private string _output = string.Empty;
    private string? _path;

    private int _present;

    public IpcLookParams()
    {
    }

    public IpcLookParams(string output, string? path)
    {
        Output = output;
        Path = path;
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

    public string? Path
    {
        get => _path;
        set => _path = value;
    }

    [JsonIgnore]
    public string? Missing => (_present & 1) == 0 ? "'output' is required" : null;

    void IIpcReusable.Reset()
    {
        _output = string.Empty;
        _path = null;
        _present = 0;
    }
}
