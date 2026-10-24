namespace Basin.Ipc;

/// <summary>
/// The reply of <c>clipboard/read</c>.
/// </summary>
/// <param name="Types">The MIME types that the selection offers. It is empty when there is no selection.</param>
/// <param name="Mime">The text type that was read, or <see langword="null"/> when nothing was read.</param>
/// <param name="Text">The text, decoded as UTF-8, or <see langword="null"/> when there is no selection or no text type.</param>
public readonly record struct IpcClipboard(ReadOnlyMemory<string> Types, string? Mime, string? Text);
