namespace Basin.Capabilities;

/// <summary>
/// A request that <see cref="IWorkspaceModel.Request"/> sends to policy.
/// </summary>
/// <param name="Kind">The action.</param>
/// <param name="Name">The name for <see cref="WorkspaceRequestKind.Create"/>, or <see langword="null"/> to let policy choose.</param>
/// <param name="GroupId">The target group for <see cref="WorkspaceRequestKind.Assign"/>, or 0.</param>
/// <param name="ToplevelId">The toplevel that <see cref="WorkspaceRequestKind.MoveToplevel"/> moves, or that <see cref="WorkspaceRequestKind.Create"/> asks policy to move, or 0.</param>
public readonly record struct WorkspaceRequest(
    WorkspaceRequestKind Kind,
    string? Name = null,
    ulong GroupId = 0,
    ulong ToplevelId = 0);
