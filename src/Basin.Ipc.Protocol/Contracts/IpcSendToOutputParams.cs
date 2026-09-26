using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcSendToOutputParams : IIpcParams, IIpcReusable
{
    private ulong _id;
    private string _output = string.Empty;

    private int _present;

    public IpcSendToOutputParams()
    {
    }

    public IpcSendToOutputParams(ulong id, string output)
    {
        Id = id;
        Output = output;
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

    public string Output
    {
        get => _output;
        set
        {
            _output = value;
            _present |= value is null ? 0 : 2;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'output' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _output = string.Empty;
        _present = 0;
    }
}
