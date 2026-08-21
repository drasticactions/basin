namespace Basin.Capabilities;

public interface IWorkspaceModel
{
    int EnumerateGroups(Span<WorkspaceGroupInfo> groups);

    int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces);

    int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs);

    int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members);

    bool Request(ulong targetId, in WorkspaceRequest request);

    void AddObserver(IWorkspaceObserver observer);

    void RemoveObserver(IWorkspaceObserver observer);
}
