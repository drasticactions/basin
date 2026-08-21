using Basin;
using Basin.Capabilities;

namespace Westonia;

internal sealed class DeferredWorkspaceModel : IWorkspaceModel
{
    private readonly List<IWorkspaceObserver> _pending = [];

    public IWorkspaceModel? Inner
    {
        get => _inner;
        set
        {
            _inner = value;
            if (value is null)
            {
                return;
            }

            foreach (var observer in _pending)
            {
                value.AddObserver(observer);
            }

            _pending.Clear();
        }
    }

    private IWorkspaceModel? _inner;

    public int EnumerateGroups(Span<WorkspaceGroupInfo> groups) => _inner?.EnumerateGroups(groups) ?? 0;

    public int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces) =>
        _inner?.EnumerateWorkspaces(groupId, workspaces) ?? 0;

    public int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs) =>
        _inner?.EnumerateGroupOutputs(groupId, outputs) ?? 0;

    public int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members) =>
        _inner?.EnumerateMembers(workspaceId, members) ?? 0;

    public bool Request(ulong targetId, in WorkspaceRequest request) =>
        _inner?.Request(targetId, request) ?? false;

    public void AddObserver(IWorkspaceObserver observer)
    {
        if (_inner is { } inner)
        {
            inner.AddObserver(observer);
            return;
        }

        _pending.Add(observer);
    }

    public void RemoveObserver(IWorkspaceObserver observer)
    {
        if (_inner is { } inner)
        {
            inner.RemoveObserver(observer);
            return;
        }

        _pending.Remove(observer);
    }
}
