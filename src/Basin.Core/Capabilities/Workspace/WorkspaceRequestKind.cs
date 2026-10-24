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
    /// <summary>Move the target workspace to the group in <see cref="WorkspaceRequest.GroupId"/>, as ext-workspace <c>assign</c> and IPC <c>workspaces/move</c> ask.</summary>
    Assign,
    /// <summary>Put the toplevel in <see cref="WorkspaceRequest.ToplevelId"/> on the target workspace, as IPC <c>windows/send-to-workspace</c> asks.</summary>
    MoveToplevel,
}
