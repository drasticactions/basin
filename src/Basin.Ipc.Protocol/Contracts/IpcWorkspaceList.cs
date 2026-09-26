namespace Basin.Ipc;

public readonly record struct IpcWorkspaceList(ReadOnlyMemory<IpcWorkspaceGroup> Groups);
