namespace Basin.Ipc;

/// <summary>
/// The reply of <c>workspaces/list</c> and the data of the <c>workspace/changed</c> event.
/// </summary>
/// <param name="Groups">Every workspace group. It is empty when the session has no workspace model.</param>
public readonly record struct IpcWorkspaceList(ReadOnlyMemory<IpcWorkspaceGroup> Groups);
