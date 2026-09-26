using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcTokenParams : IIpcParams, IIpcReusable
{
    private string _token = string.Empty;

    private int _present;

    public IpcTokenParams()
    {
    }

    public IpcTokenParams(string token)
    {
        Token = token;
    }

    public string Token
    {
        get => _token;
        set
        {
            _token = value;
            _present |= value is null ? 0 : 1;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'token' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _token = string.Empty;
        _present = 0;
    }
}
