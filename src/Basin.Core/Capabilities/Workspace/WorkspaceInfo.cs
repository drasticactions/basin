namespace Basin.Capabilities;

/// <summary>
/// One workspace, as <see cref="IWorkspaceModel.EnumerateWorkspaces"/> reports it.
/// </summary>
/// <param name="Id">The workspace id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Handle">An id that stays the same across sessions, as the ext-workspace id event sends it, or <see langword="null"/>.</param>
/// <param name="State">The state flags.</param>
/// <param name="Coordinates">The position of the workspace in the grid of its group, or <see langword="null"/> for none.</param>
public readonly record struct WorkspaceInfo(
    ulong Id,
    string Name,
    string? Handle,
    WorkspaceStateFlags State,
    uint[]? Coordinates = null);
