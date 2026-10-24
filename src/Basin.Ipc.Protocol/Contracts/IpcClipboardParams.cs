using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>clipboard/read</c>. Every key is optional.
/// </summary>
public sealed class IpcClipboardParams : IIpcParams, IIpcReusable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpcClipboardParams"/> class. Every key starts absent.
    /// </summary>
    public IpcClipboardParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcClipboardParams"/> class.
    /// </summary>
    /// <param name="kind">The selection, or null.</param>
    /// <param name="mime">The text type, or null.</param>
    /// <param name="timeoutMs">The read timeout in milliseconds, or null.</param>
    /// <param name="maxBytes">The byte limit, or null.</param>
    public IpcClipboardParams(string? kind = null, string? mime = null, long? timeoutMs = null, long? maxBytes = null)
    {
        Kind = kind;
        Mime = mime;
        TimeoutMs = timeoutMs;
        MaxBytes = maxBytes;
    }

    /// <summary>
    /// Gets or sets the selection: <c>clipboard</c> or <c>primary</c>. <see langword="null"/> means <c>clipboard</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    /// <summary>
    /// Gets or sets the text type to read. It must be <c>text/*</c>, <c>UTF8_STRING</c>, <c>STRING</c> or <c>TEXT</c>. <see langword="null"/> takes the first offered text type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mime { get; set; }

    /// <summary>
    /// Gets or sets the longest wait for the owner to send the text, in milliseconds, at most 120000. <see langword="null"/> means 2000.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TimeoutMs { get; set; }

    /// <summary>
    /// Gets or sets the most bytes to read, at most 16 MiB. A larger text is <c>too_large</c>. <see langword="null"/> means 1 MiB.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxBytes { get; set; }

    /// <summary>
    /// Gets <see langword="null"/> always, because every key of this method is optional.
    /// </summary>
    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Kind = null;
        Mime = null;
        TimeoutMs = null;
        MaxBytes = null;
    }
}
