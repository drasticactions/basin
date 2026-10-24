using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>clipboard/write</c>.
/// </summary>
public sealed class IpcClipboardWriteParams : IIpcParams, IIpcReusable
{
    private string _text = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcClipboardWriteParams"/> class. Every key starts absent, so the deserializer can fill it.
    /// </summary>
    public IpcClipboardWriteParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcClipboardWriteParams"/> class.
    /// </summary>
    /// <param name="text">The text to offer.</param>
    /// <param name="kind">The selection, or null.</param>
    public IpcClipboardWriteParams(string text, string? kind = null)
    {
        Text = text;
        Kind = kind;
    }

    /// <summary>
    /// Gets or sets the text that the compositor offers as UTF-8 text until another owner replaces it. The key is required.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            _present |= value is null ? 0 : 1;
        }
    }

    /// <summary>
    /// Gets or sets the selection: <c>clipboard</c> or <c>primary</c>. <see langword="null"/> means <c>clipboard</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'text' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _text = string.Empty;
        _present = 0;
        Kind = null;
    }
}
