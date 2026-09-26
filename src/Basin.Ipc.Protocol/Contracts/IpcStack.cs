namespace Basin.Ipc;

public readonly record struct IpcStack(ReadOnlyMemory<ulong> Ids);
