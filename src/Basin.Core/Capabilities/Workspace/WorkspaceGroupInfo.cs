namespace Basin.Capabilities;

/// <summary>
/// One workspace group, for example the workspaces of one output.
/// </summary>
/// <param name="Id">The group id.</param>
/// <param name="ClientsCanCreateWorkspaces">Whether a client can ask for a new workspace in the group.</param>
public readonly record struct WorkspaceGroupInfo(ulong Id, bool ClientsCanCreateWorkspaces);
