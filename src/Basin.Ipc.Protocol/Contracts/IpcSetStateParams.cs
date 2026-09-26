using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcSetStateParams : IIpcParams, IIpcReusable
{
    private ulong _id;

    private int _present;

    public IpcSetStateParams()
    {
    }

    public IpcSetStateParams(
        ulong id,
        bool? maximized = null,
        bool? minimized = null,
        bool? fullscreen = null,
        bool? noBorder = null)
    {
        Id = id;
        Maximized = maximized;
        Minimized = minimized;
        Fullscreen = fullscreen;
        NoBorder = noBorder;
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Maximized { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Minimized { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Fullscreen { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NoBorder { get; set; }

    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _present = 0;
        Maximized = null;
        Minimized = null;
        Fullscreen = null;
        NoBorder = null;
    }
}
