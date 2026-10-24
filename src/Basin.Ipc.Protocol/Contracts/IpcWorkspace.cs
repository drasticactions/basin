namespace Basin.Ipc;

/// <summary>
/// One workspace in an <see cref="IpcWorkspaceGroup"/>.
/// </summary>
/// <param name="Id">The workspace id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Active"><see langword="true"/> when the workspace is shown.</param>
/// <param name="Urgent"><see langword="true"/> when the workspace wants attention.</param>
/// <param name="Hidden"><see langword="true"/> when a workspace switcher must not show the workspace.</param>
/// <param name="Coordinates">The position of the workspace in the grid of its group. It is empty when the workspace has no position.</param>
/// <param name="Members">The ids of the windows on the workspace.</param>
public readonly record struct IpcWorkspace(
    ulong Id,
    string Name,
    bool Active,
    bool Urgent,
    bool Hidden,
    ReadOnlyMemory<uint> Coordinates,
    ReadOnlyMemory<ulong> Members);
