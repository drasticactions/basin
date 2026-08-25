using Basin;
using Basin.Capabilities;

namespace PlasmaHost;

internal sealed class PlasmaHostDesktops : IWorkspaceModel
{
    private const ulong GroupId = 1;

    private readonly WorkspaceObservers _observers = new();
    private readonly WorkspaceSet<PlasmaHostDesktop> _desktops = new();
    private readonly Dictionary<PlasmaHostView, ulong> _homes = [];
    private readonly OutputLayout _layout;

    public PlasmaHostDesktops(OutputLayout layout, int count)
    {
        _layout = layout;
        _desktops.IdOf = desktop => desktop.Id;
        _desktops.Describe = desktop => new WorkspaceInfo(
            desktop.Id,
            desktop.Name,
            desktop.Handle,
            ReferenceEquals(desktop, _desktops.Active) ? WorkspaceStateFlags.Active : WorkspaceStateFlags.None,
            [(uint)_desktops.IndexOf(desktop)]);
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            _desktops.Add(new PlasmaHostDesktop(_desktops.NextId(), $"Desktop {i + 1}"));
        }
    }

    public event Action? Changed;

    public IReadOnlyList<PlasmaHostDesktop> Desktops => _desktops;

    public int Current => Math.Max(0, _desktops.ActiveIndex);

    public IToplevelModel? Toplevels { get; set; }

    public PlasmaHostWindows? Windows { get; set; }

    public int IndexOf(PlasmaHostView view)
    {
        if (!_homes.TryGetValue(view, out var id))
        {
            return Current;
        }

        for (var i = 0; i < _desktops.Count; i++)
        {
            if (_desktops[i].Id == id)
            {
                return i;
            }
        }

        return Current;
    }

    public void Adopt(PlasmaHostView view)
    {
        _homes[view] = _desktops[Current].Id;
        ApplyVisibility(view);
        _observers.MembersChanged();
    }

    public void Forget(PlasmaHostView view)
    {
        if (_homes.Remove(view))
        {
            _observers.MembersChanged();
        }
    }

    public void MoveTo(PlasmaHostView view, int index)
    {
        if (index < 0 || index >= _desktops.Count)
        {
            return;
        }

        _homes[view] = _desktops[index].Id;
        ApplyVisibility(view);
        _observers.MembersChanged();
        Changed?.Invoke();
    }

    public void Activate(int index)
    {
        if (index < 0 || index >= _desktops.Count || index == Current)
        {
            return;
        }

        _desktops.Active = _desktops[index];
        foreach (var view in ViewList)
        {
            ApplyVisibility(view);
        }

        _observers.Changed();
        Changed?.Invoke();
    }

    public void Step(int delta) =>
        Activate(((Current + delta) % _desktops.Count + _desktops.Count) % _desktops.Count);

    public int EnumerateGroups(Span<WorkspaceGroupInfo> groups)
    {
        if (groups.Length < 1)
        {
            return -1;
        }

        groups[0] = new WorkspaceGroupInfo(GroupId, ClientsCanCreateWorkspaces: true);
        return 1;
    }

    public int EnumerateWorkspaces(ulong groupId, Span<WorkspaceInfo> workspaces)
    {
        if (groupId != GroupId)
        {
            return 0;
        }

        return _desktops.Fill(workspaces);
    }

    public int EnumerateGroupOutputs(ulong groupId, Span<IOutput> outputs)
    {
        if (groupId != GroupId)
        {
            return 0;
        }

        var count = 0;
        foreach (var (output, _) in _layout.Outputs)
        {
            if (count == outputs.Length)
            {
                return -1;
            }

            outputs[count++] = output;
        }

        return count;
    }

    public int EnumerateMembers(ulong workspaceId, Span<WorkspaceMember> members)
    {
        if (Toplevels is not { } toplevels || Windows is not { } windows || IndexById(workspaceId) is not { } index)
        {
            return 0;
        }

        var count = 0;
        Span<ToplevelInfo> all = new ToplevelInfo[64];
        var total = toplevels.Enumerate(all);
        for (var i = 0; i < total && i < all.Length; i++)
        {
            if (all[i].Surface is not { } surface || windows.ViewFor(surface) is not { } view ||
                IndexOf(view) != index)
            {
                continue;
            }

            if (count == members.Length)
            {
                return -1;
            }

            var box = windows.FrameBoxOf(view);
            members[count++] = new WorkspaceMember(all[i].Id, box);
        }

        return count;
    }

    public bool Request(ulong targetId, in WorkspaceRequest request)
    {
        switch (request.Kind)
        {
            case WorkspaceRequestKind.Activate when IndexById(targetId) is { } index:
                Activate(index);
                return true;

            case WorkspaceRequestKind.Create when targetId == GroupId:
                _desktops.Add(new PlasmaHostDesktop(
                    _desktops.NextId(),
                    request.Name is { Length: > 0 } name ? name : $"Desktop {_desktops.Count + 1}"));
                _observers.Changed();
                Changed?.Invoke();
                return true;

            case WorkspaceRequestKind.Remove when IndexById(targetId) is { } index && _desktops.Count > 1:
                Remove(index);
                return true;

            case WorkspaceRequestKind.Move when IndexById(targetId) is { } index:
                if (ViewOfToplevel(request.ToplevelId) is { } moved)
                {
                    MoveTo(moved, index);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    public void AddObserver(IWorkspaceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IWorkspaceObserver observer) => _observers.Remove(observer);

    private void Remove(int index)
    {
        var dead = _desktops[index];
        _desktops.RemoveAt(index);
        var survivor = _desktops[Math.Min(index, _desktops.Count - 1)].Id;
        foreach (var (view, home) in _homes)
        {
            if (home == dead.Id)
            {
                _homes[view] = survivor;
            }
        }

        foreach (var view in ViewList)
        {
            ApplyVisibility(view);
        }

        _observers.Changed();
        Changed?.Invoke();
    }

    private IReadOnlyList<PlasmaHostView> ViewList => Windows?.Views ?? [];

    private PlasmaHostView? ViewOfToplevel(ulong toplevelId)
    {
        if (Toplevels is not { } toplevels || !toplevels.TryGet(toplevelId, out var info) ||
            info.Surface is not { } surface)
        {
            return null;
        }

        return Windows?.ViewFor(surface);
    }

    private int? IndexById(ulong id) =>
        _desktops.ById(id) is { } desktop ? _desktops.IndexOf(desktop) : null;

    private void ApplyVisibility(PlasmaHostView view) =>
        view.Tree.Enabled = IndexOf(view) == Current && !view.Minimized;
}
