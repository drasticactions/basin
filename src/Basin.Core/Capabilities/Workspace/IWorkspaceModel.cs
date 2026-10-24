namespace Basin.Capabilities;

/// <summary>
/// The workspaces of a session in groups, as ext-workspace and IPC see them. Policy owns the model and decides each request.
/// </summary>
public interface IWorkspaceModel
{
    /// <summary>
    /// Writes every workspace group into a span.
    /// </summary>
    /// <param name="groups">The destination. It must have space for every group.</param>
    /// <returns>The number of groups written, or -1 when the span is too small.</returns>
    int EnumerateGroups(Span<WorkspaceGroupInfo> groups);

    /// <summary>
    /// Writes the workspaces of one group into a span, in display order.
    /// </summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="workspaces">The destination. It must have space for every workspace of the group.</param>
    /// <returns>The number of workspaces written, 0 for an unknown group, or -1 when the span is too small.</returns>
    int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces);

    /// <summary>
    /// Writes the outputs of one group into a span.
    /// </summary>
    /// <param name="groupId">The group id.</param>
    /// <param name="outputs">The destination. It must have space for every output of the group.</param>
    /// <returns>The number of outputs written, 0 for an unknown group, or -1 when the span is too small.</returns>
    int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs);

    /// <summary>
    /// Writes the toplevels on one workspace into a span.
    /// </summary>
    /// <param name="workspaceId">The workspace id.</param>
    /// <param name="members">The destination. It must have space for every member.</param>
    /// <returns>The number of members written, 0 for an unknown workspace, or -1 when the span is too small.</returns>
    int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members);

    /// <summary>
    /// Asks policy to act on a workspace or a group.
    /// </summary>
    /// <param name="targetId">A group id for <see cref="WorkspaceRequestKind.Create"/>, and a workspace id for the other kinds.</param>
    /// <param name="request">The action and its arguments.</param>
    /// <returns><see langword="true"/> when policy accepted the request.</returns>
    bool Request(ulong targetId, in WorkspaceRequest request);

    /// <summary>
    /// Adds an observer for changes of the groups, the workspaces and their members.
    /// </summary>
    /// <param name="observer">The observer to add.</param>
    void AddObserver(IWorkspaceObserver observer);

    /// <summary>
    /// Removes an observer. The call has no effect when the observer is not present.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    void RemoveObserver(IWorkspaceObserver observer);
}
