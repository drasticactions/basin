using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcClipboardWriteParams : IIpcParams, IIpcReusable
{
    private string _text = string.Empty;

    private int _present;

    public IpcClipboardWriteParams()
    {
    }

    public IpcClipboardWriteParams(string text, string? kind = null)
    {
        Text = text;
        Kind = kind;
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'text' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _text = string.Empty;
        _present = 0;
        Kind = null;
    }
}
