namespace Basin.Ipc;

public readonly record struct IpcWorkspaceGroup(
    ulong Id,
    bool CanCreate,
    ReadOnlyMemory<string> Outputs,
    ReadOnlyMemory<IpcWorkspace> Workspaces);
