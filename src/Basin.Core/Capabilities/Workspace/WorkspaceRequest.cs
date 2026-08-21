namespace Basin.Capabilities;

public readonly record struct WorkspaceRequest(
    WorkspaceRequestKind Kind,
    string? Name = null,
    ulong GroupId = 0,
    ulong ToplevelId = 0);
