namespace Basin.Capabilities;

public readonly record struct WorkspaceInfo(
    ulong Id,
    string Name,
    string? Handle,
    WorkspaceStateFlags State,
    uint[]? Coordinates = null);
