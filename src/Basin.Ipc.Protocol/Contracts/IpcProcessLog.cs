namespace Basin.Ipc;

public readonly record struct IpcProcessLog(long LaunchId, string Path, ReadOnlyMemory<string> Lines);
