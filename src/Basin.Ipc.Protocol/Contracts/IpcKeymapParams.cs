using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcKeymapParams : IIpcParams, IIpcReusable
{
    public IpcKeymapParams()
    {
    }

    public IpcKeymapParams(bool? text = null, bool? fd = null)
    {
        Text = text;
        Fd = fd;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Fd { get; set; }

    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Text = null;
        Fd = null;
    }
}
