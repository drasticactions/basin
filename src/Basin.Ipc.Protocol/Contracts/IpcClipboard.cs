namespace Basin.Ipc;

public readonly record struct IpcClipboard(ReadOnlyMemory<string> Types, string? Mime, string? Text);
