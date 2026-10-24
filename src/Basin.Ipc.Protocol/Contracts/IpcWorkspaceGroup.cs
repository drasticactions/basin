namespace Basin.Ipc;

/// <summary>
/// One workspace group in the reply of <c>workspaces/list</c>.
/// </summary>
/// <param name="Id">The group id.</param>
/// <param name="CanCreate"><see langword="true"/> when a client can ask for a new workspace in the group.</param>
/// <param name="Outputs">The names of the outputs that the group covers.</param>
/// <param name="Workspaces">The workspaces of the group.</param>
public readonly record struct IpcWorkspaceGroup(
    ulong Id,
    bool CanCreate,
    ReadOnlyMemory<string> Outputs,
    ReadOnlyMemory<IpcWorkspace> Workspaces);
