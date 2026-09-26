namespace Basin.Ipc;

public readonly record struct IpcLineReply(ReadOnlyMemory<string> Lines);
