namespace Basin.Capabilities;

public sealed class EmptyWorkspaceModel : IWorkspaceModel
{
    public static EmptyWorkspaceModel Instance { get; } = new();

    public void AddObserver(IWorkspaceObserver observer)
    {
    }

    public void RemoveObserver(IWorkspaceObserver observer)
    {
    }

    public int EnumerateGroups(Span<WorkspaceGroupInfo> groups) => 0;

    public int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces) => 0;

    public int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs) => 0;

    public int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members) => 0;

    public bool Request(ulong targetId, in WorkspaceRequest request) => false;
}
