namespace Basin.Capabilities;

/// <summary>
/// An <see cref="IWorkspaceModel"/> with no groups and no workspaces. It declines every request and never notifies an observer.
/// </summary>
public sealed class EmptyWorkspaceModel : IWorkspaceModel
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static EmptyWorkspaceModel Instance { get; } = new();

    /// <summary>
    /// Ignores the observer, because this model never changes.
    /// </summary>
    /// <param name="observer">The observer, which never receives a call.</param>
    public void AddObserver(IWorkspaceObserver observer)
    {
    }

    /// <summary>
    /// Has no effect, because this model holds no observers.
    /// </summary>
    /// <param name="observer">The observer to remove.</param>
    public void RemoveObserver(IWorkspaceObserver observer)
    {
    }

    /// <summary>
    /// Writes no groups.
    /// </summary>
    /// <param name="groups">The destination, which stays unchanged.</param>
    /// <returns>Always 0.</returns>
    public int EnumerateGroups(Span<WorkspaceGroupInfo> groups) => 0;

    /// <summary>
    /// Writes no workspaces.
    /// </summary>
    /// <param name="groupId">The group id, which matches no group.</param>
    /// <param name="workspaces">The destination, which stays unchanged.</param>
    /// <returns>Always 0.</returns>
    public int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces) => 0;

    /// <summary>
    /// Writes no outputs.
    /// </summary>
    /// <param name="groupId">The group id, which matches no group.</param>
    /// <param name="outputs">The destination, which stays unchanged.</param>
    /// <returns>Always 0.</returns>
    public int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs) => 0;

    /// <summary>
    /// Writes no members.
    /// </summary>
    /// <param name="workspaceId">The workspace id, which matches no workspace.</param>
    /// <param name="members">The destination, which stays unchanged.</param>
    /// <returns>Always 0.</returns>
    public int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members) => 0;

    /// <summary>
    /// Declines every request.
    /// </summary>
    /// <param name="targetId">The target id, which matches nothing.</param>
    /// <param name="request">The declined request.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool Request(ulong targetId, in WorkspaceRequest request) => false;
}
