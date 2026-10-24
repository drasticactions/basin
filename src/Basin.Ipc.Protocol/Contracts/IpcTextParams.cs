using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>input/text</c> and <c>seat/text</c>. The <c>text</c> key is required.
/// </summary>
public sealed class IpcTextParams : IIpcParams, IIpcReusable
{
    private string _text = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcTextParams"/> class.
    /// Every required key starts absent. The deserializer uses this constructor.
    /// </summary>
    public IpcTextParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcTextParams"/> class.
    /// The <c>text</c> key is present.
    /// </summary>
    /// <param name="text">The text to type.</param>
    public IpcTextParams(string text)
    {
        Text = text;
    }

    /// <summary>
    /// Gets or sets the text to type. A character that the chosen path cannot produce is <c>invalid_params</c>, and nothing is sent.
    /// An explicit JSON <see langword="null"/> does not count as present.
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
    /// Gets or sets the path to force: <c>auto</c>, <c>keymap</c> or <c>text-input</c>.
    /// Absent means <c>auto</c>, which uses an enabled text input first. <c>text-input</c> with no enabled text input is <c>refused</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Via { get; set; }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'text' is required."
        : null;

    void IIpcReusable.Reset()
    {
        _text = string.Empty;
        _present = 0;
        Via = null;
    }
}
