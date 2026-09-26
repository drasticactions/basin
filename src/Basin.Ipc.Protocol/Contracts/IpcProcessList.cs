namespace Basin.Ipc;

public readonly record struct IpcProcessList(ReadOnlyMemory<IpcProcess> Processes);
