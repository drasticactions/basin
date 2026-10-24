namespace Basin.Capabilities;

/// <summary>
/// A request that <see cref="IWorkspaceModel.Request"/> sends to policy.
/// </summary>
/// <param name="Kind">The action.</param>
/// <param name="Name">The name for <see cref="WorkspaceRequestKind.Create"/>, or <see langword="null"/> to let policy choose.</param>
/// <param name="GroupId">The target group for <see cref="WorkspaceRequestKind.Assign"/> or <see cref="WorkspaceRequestKind.Move"/>, or 0.</param>
/// <param name="ToplevelId">The toplevel that the request moves, or 0.</param>
public readonly record struct WorkspaceRequest(
    WorkspaceRequestKind Kind,
    string? Name = null,
    ulong GroupId = 0,
    ulong ToplevelId = 0);
