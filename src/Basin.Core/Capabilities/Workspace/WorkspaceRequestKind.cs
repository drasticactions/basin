namespace Basin.Capabilities;

/// <summary>
/// The actions that a <see cref="WorkspaceRequest"/> can ask for.
/// </summary>
public enum WorkspaceRequestKind
{
    /// <summary>Show the target workspace.</summary>
    Activate,
    /// <summary>Hide the target workspace.</summary>
    Deactivate,
    /// <summary>Remove the target workspace.</summary>
    Remove,
    /// <summary>Create a workspace in the target group. A toplevel id also asks policy to move that toplevel to the new workspace.</summary>
    Create,
    /// <summary>
    /// With a group id, move the target workspace to that group, as ext-workspace asks.
    /// With a toplevel id, put that toplevel on the target workspace, as IPC asks.
    /// </summary>
    Assign,
    /// <summary>Move the target workspace to the group in <see cref="WorkspaceRequest.GroupId"/>, as IPC asks.</summary>
    Move,
}
