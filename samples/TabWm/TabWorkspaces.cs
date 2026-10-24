using Basin;
using Basin.Capabilities;

namespace TabWm;

internal sealed class TabWorkspaces : IWorkspaceModel
{
    private readonly WorkspaceObservers _observers = new();
    private readonly TabWm _shell;

    public TabWorkspaces(TabWm shell)
    {
        _shell = shell;
    }

    public void Changed() => _observers.Changed();

    public void MembersChanged() => _observers.MembersChanged();

    public int EnumerateGroups(Span<WorkspaceGroupInfo> groups)
    {
        var sets = _shell.Sets;
        var count = Math.Min(groups.Length, sets.Count);
        for (var i = 0; i < count; i++)
        {
            groups[i] = new WorkspaceGroupInfo(sets[i].Id, ClientsCanCreateWorkspaces: true);
        }

        return count;
    }

    public int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces)
    {
        if (SetOf(groupId) is not { } set)
        {
            return 0;
        }

        var count = Math.Min(workspaces.Length, set.Tabs.Count);
        for (var i = 0; i < count; i++)
        {
            var tab = set.Tabs[i];
            var state = ReferenceEquals(set.Active, tab) ? WorkspaceStateFlags.Active : WorkspaceStateFlags.None;
            if (tab.Attention)
            {
                state |= WorkspaceStateFlags.Urgent;
            }

            workspaces[i] = new WorkspaceInfo(tab.Id, tab.Label, $"tab-wm-tab-{tab.Id}", state, [(uint)i]);
        }

        return count;
    }

    public int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs)
    {
        if (SetOf(groupId) is not { } set || outputs.Length == 0)
        {
            return 0;
        }

        outputs[0] = set.Output;
        return 1;
    }

    public int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members)
    {
        if (_shell.FindTab(workspaceId) is not { } tab)
        {
            return 0;
        }

        var count = 0;
        foreach (var window in tab.Windows)
        {
            if (count >= members.Length)
            {
                break;
            }

            if (window.Mapped && ToplevelId(window) is { } id)
            {
                members[count++] = new WorkspaceMember(id, window.Geometry);
            }
        }

        return count;
    }

    public bool Request(ulong targetId, in WorkspaceRequest request)
    {
        switch (request.Kind)
        {
            case WorkspaceRequestKind.Activate when _shell.FindTab(targetId) is { } tab:
                return _shell.SelectTab(tab);
            case WorkspaceRequestKind.Create when SetOf(targetId) is { } set:
                _shell.NewTab(set, select: false, label: request.Name is { Length: > 0 } name ? name : null);
                return true;
            case WorkspaceRequestKind.Remove when _shell.FindTab(targetId) is { } tab:
                _shell.CloseTab(tab);
                return true;
            case WorkspaceRequestKind.Assign when _shell.FindTab(targetId) is { } tab && SetOf(request.GroupId) is { } group:
                _shell.MoveTab(tab, group, group.Tabs.Count, select: false);
                return true;
            case WorkspaceRequestKind.MoveToplevel when _shell.FindTab(targetId) is { } tab && WindowOf(request.ToplevelId) is { } window:
                _shell.MoveWindow(window, tab, follow: false);
                return true;
            default:
                return false;
        }
    }

    public void AddObserver(IWorkspaceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IWorkspaceObserver observer) => _observers.Remove(observer);

    private TabSet? SetOf(ulong id)
    {
        foreach (var set in _shell.Sets)
        {
            if (set.Id == id)
            {
                return set;
            }
        }

        return null;
    }

    private ulong? ToplevelId(TabWindow window) =>
        window.Surface is { } surface && _shell.Toplevels?.TryFind(surface, out var info) == true ? info.Id : null;

    private TabWindow? WindowOf(ulong toplevelId)
    {
        if (_shell.Toplevels is not { } model || !model.TryGet(toplevelId, out var info) || info.Surface is not { } surface)
        {
            return null;
        }

        return _shell.WindowOwning(surface);
    }
}
