using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcTextParams : IIpcParams, IIpcReusable
{
    private string _text = string.Empty;

    private int _present;

    public IpcTextParams()
    {
    }

    public IpcTextParams(string text)
    {
        Text = text;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            _present |= value is null ? 0 : 1;
        }
    }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'text' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _text = string.Empty;
        _present = 0;
    }
}
