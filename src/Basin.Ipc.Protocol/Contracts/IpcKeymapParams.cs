using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>keyboard/keymap</c>. Every key is optional.
/// </summary>
public sealed class IpcKeymapParams : IIpcParams, IIpcReusable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpcKeymapParams"/> class.
    /// Every key starts absent.
    /// </summary>
    public IpcKeymapParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcKeymapParams"/> class.
    /// A <see langword="null"/> argument leaves its key out of the request.
    /// </summary>
    /// <param name="text">Whether the reply carries the keymap text.</param>
    /// <param name="fd">Whether the reply carries a duplicate of the keymap memfd.</param>
    public IpcKeymapParams(bool? text = null, bool? fd = null)
    {
        Text = text;
        Fd = fd;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the reply carries the keymap text in <see cref="IpcKeymapInfo.Text"/>.
    /// Absent means no text.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Text { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the reply carries a duplicate of the keymap memfd as a descriptor.
    /// Absent means no descriptor.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Fd { get; set; }

    /// <summary>
    /// Gets <see langword="null"/> at all times, because this request has no required key.
    /// </summary>
    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Text = null;
        Fd = null;
    }
}
