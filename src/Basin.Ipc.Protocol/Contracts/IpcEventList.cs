namespace Basin.Ipc;

public readonly record struct IpcEventList(ReadOnlyMemory<string> Events);
