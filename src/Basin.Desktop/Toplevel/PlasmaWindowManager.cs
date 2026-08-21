using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class PlasmaWindowManager : IToplevelObserver, IWorkspaceObserver, IDisposable
{
    public const int Version = 16;

    private readonly WlGlobal _global;
    private readonly IToplevelModel? _toplevels;
    private readonly IWorkspaceModel? _workspaces;
    private readonly List<OrgKdePlasmaWindowManagementResource> _managers = [];
    private readonly Dictionary<ulong, Tracked> _windows = [];
    private List<(ulong WorkspaceId, string Handle, ulong GroupId)> _desktops = [];

    private sealed class Tracked
    {
        public required ulong Id;
        public required string Uuid;
        public readonly List<OrgKdePlasmaWindowResource> Resources = [];
        public string? Desktop;
        public Box Box;
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);

    public PlasmaWindowManager(WlServerDisplay display, IToplevelModel? toplevels, IWorkspaceModel? workspaces)
    {
        ArgumentNullException.ThrowIfNull(display);
        _toplevels = toplevels;
        _workspaces = workspaces;
        _global = display.CreateGlobal(OrgKdePlasmaWindowManagement.Interface, Version, OnBind);

        if (_toplevels is { } model)
        {
            model.AddObserver(this);

            var infos = new ToplevelInfo[16];
            var count = model.Enumerate(infos);
            while (count < 0)
            {
                infos = new ToplevelInfo[infos.Length * 2];
                count = model.Enumerate(infos);
            }

            for (var i = 0; i < count; i++)
            {
                Track(infos[i].Id);
            }
        }

        if (_workspaces is { } live)
        {
            live.AddObserver(this);
            RefreshDesktops();
            RefreshPlacements(apply: false);
        }
    }

    public void OnWorkspacesChanged() => OnWorkspacesChangedCore();

    public void OnWorkspaceMembersChanged() => OnMembersChanged();

    public void OnToplevelAdded(ulong toplevelId) => OnAdded(toplevelId);

    public void OnToplevelChanged(ulong toplevelId) => OnChanged(toplevelId);

    public void OnToplevelRemoved(ulong toplevelId) => OnRemoved(toplevelId);

    public void Dispose()
    {
        if (_toplevels is { } model)
        {
            model.RemoveObserver(this);
        }

        if (_workspaces is { } live)
        {
            live.RemoveObserver(this);
        }

        _global.Dispose();
    }

    private Tracked Track(ulong id)
    {
        var tracked = new Tracked { Id = id, Uuid = $"basin-{id}" };
        _windows[id] = tracked;
        return tracked;
    }

    private void OnAdded(ulong id)
    {
        if (_windows.ContainsKey(id))
        {
            return;
        }

        var tracked = Track(id);
        foreach (var manager in _managers)
        {
            if (!manager.IsDestroyed)
            {
                Announce(manager, tracked);
            }
        }
    }

    private void OnChanged(ulong id)
    {
        if (!_windows.TryGetValue(id, out var tracked) ||
            _toplevels is not { } model || !model.TryGet(id, out var info))
        {
            return;
        }

        foreach (var resource in tracked.Resources)
        {
            if (!resource.IsDestroyed)
            {
                resource.SendTitleChanged(info.Title);
                resource.SendAppIdChanged(info.AppId);
                resource.SendStateChanged(StateBits(info.State));
            }
        }
    }

    private void OnRemoved(ulong id)
    {
        if (_windows.Remove(id, out var tracked))
        {
            foreach (var resource in tracked.Resources)
            {
                if (!resource.IsDestroyed)
                {
                    resource.SendUnmapped();
                }
            }
        }
    }

    private void OnWorkspacesChangedCore()
    {
        RefreshDesktops();
        RefreshPlacements(apply: true);
    }

    private void OnMembersChanged() => RefreshPlacements(apply: true);

    private void RefreshDesktops()
    {
        var result = new List<(ulong, string, ulong)>();
        if (_workspaces is { } model)
        {
            var groups = new WorkspaceGroupInfo[8];
            var groupCount = model.EnumerateGroups(groups);
            while (groupCount < 0)
            {
                groups = new WorkspaceGroupInfo[groups.Length * 2];
                groupCount = model.EnumerateGroups(groups);
            }

            var workspaces = new WorkspaceInfo[16];
            for (var i = 0; i < groupCount; i++)
            {
                var count = model.EnumerateWorkspaces(groups[i].Id, workspaces);
                while (count < 0)
                {
                    workspaces = new WorkspaceInfo[workspaces.Length * 2];
                    count = model.EnumerateWorkspaces(groups[i].Id, workspaces);
                }

                for (var w = 0; w < count; w++)
                {
                    result.Add((workspaces[w].Id, workspaces[w].Handle ?? $"ws-{workspaces[w].Id}", groups[i].Id));
                }
            }
        }

        _desktops = result;
    }

    private void RefreshPlacements(bool apply)
    {
        if (_workspaces is not { } model)
        {
            return;
        }

        var placements = new Dictionary<ulong, (string Desktop, Box Box)>();
        var members = new WorkspaceMember[16];
        foreach (var (workspaceId, handle, _) in _desktops)
        {
            var count = model.EnumerateMembers(workspaceId, members);
            while (count < 0)
            {
                members = new WorkspaceMember[members.Length * 2];
                count = model.EnumerateMembers(workspaceId, members);
            }

            for (var i = 0; i < count; i++)
            {
                placements[members[i].ToplevelId] = (handle, members[i].Geometry);
            }
        }

        foreach (var tracked in _windows.Values)
        {
            var placed = placements.TryGetValue(tracked.Id, out var place);
            var desktop = placed ? place.Desktop : null;
            var box = placed ? place.Box : default;
            if (apply)
            {
                foreach (var resource in tracked.Resources)
                {
                    if (resource.IsDestroyed)
                    {
                        continue;
                    }

                    if (box != tracked.Box && placed && resource.Version >= 6)
                    {
                        resource.SendGeometry(box.X, box.Y, (uint)box.Width, (uint)box.Height);
                    }

                    if (desktop != tracked.Desktop && resource.Version >= 8)
                    {
                        if (tracked.Desktop is { } old)
                        {
                            resource.SendVirtualDesktopLeft(old);
                        }

                        if (desktop is not null)
                        {
                            resource.SendVirtualDesktopEntered(desktop);
                        }
                    }
                }
            }

            tracked.Desktop = desktop;
            tracked.Box = box;
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdePlasmaWindowManagementResource(client, version, id);
        _managers.Add(manager);
        manager.Destroyed += (_, _) => _managers.Remove(manager);

        manager.GetWindowByUuid += (_, e) =>
        {
            var resource = new OrgKdePlasmaWindowResource(client, manager.Version, e.Id);
            Tracked? tracked = null;
            foreach (var candidate in _windows.Values)
            {
                if (candidate.Uuid == e.InternalWindowUuid)
                {
                    tracked = candidate;
                    break;
                }
            }

            WireWindow(tracked, resource);
        };
        manager.GetWindow += (_, e) =>
        {
            var resource = new OrgKdePlasmaWindowResource(client, manager.Version, e.Id);
            _windows.TryGetValue(e.InternalWindowId, out var tracked);
            WireWindow(tracked, resource);
        };

        manager.SendShowDesktopChanged(0);
        if (version >= 12)
        {
            var uuids = new List<string>(_windows.Count);
            foreach (var tracked in _windows.Values)
            {
                uuids.Add(tracked.Uuid);
            }

            manager.SendStackingOrderUuidChanged(string.Join(';', uuids));
        }

        foreach (var tracked in _windows.Values)
        {
            Announce(manager, tracked);
        }
    }

    private static void Announce(OrgKdePlasmaWindowManagementResource manager, Tracked tracked)
    {
        if (manager.Version >= 13)
        {
            manager.SendWindowWithUuid((uint)tracked.Id, tracked.Uuid);
        }
        else
        {
            manager.SendWindow((uint)tracked.Id);
        }
    }

    private void WireWindow(Tracked? tracked, OrgKdePlasmaWindowResource resource)
    {
        resource.GetIcon += (_, e) => CloseFd(e.Fd);
        if (tracked is null)
        {
            resource.SendUnmapped();
            return;
        }

        tracked.Resources.Add(resource);
        resource.Destroyed += (_, _) => tracked.Resources.Remove(resource);

        var id = tracked.Id;
        resource.Close += (_, _) => _toplevels?.Request(id, new ToplevelRequest(ToplevelRequestKind.Close));
        resource.SetState += (_, e) => ApplyState(id, e.Flags, e.State);
        resource.RequestEnterVirtualDesktop += (_, e) => MoveTo(e.Id, id);
        resource.RequestEnterNewVirtualDesktop += (_, _) => CreateAndAdopt(id);
        resource.SetVirtualDesktop += (_, e) =>
        {
            if (e.Number < _desktops.Count)
            {
                MoveTo(_desktops[(int)e.Number].Handle, id);
            }
        };

        if (_toplevels is { } model && model.TryGet(id, out var info))
        {
            resource.SendTitleChanged(info.Title);
            resource.SendAppIdChanged(info.AppId);
            resource.SendStateChanged(StateBits(info.State));
        }

        if (tracked.Box.Width > 0 && resource.Version >= 6)
        {
            resource.SendGeometry(tracked.Box.X, tracked.Box.Y, (uint)tracked.Box.Width, (uint)tracked.Box.Height);
        }

        if (tracked.Desktop is { } desktop && resource.Version >= 8)
        {
            resource.SendVirtualDesktopEntered(desktop);
        }

        if (resource.Version >= 4)
        {
            resource.SendInitialState();
        }
    }

    private void ApplyState(ulong id, uint flags, uint state)
    {
        if (_toplevels is not { } model)
        {
            return;
        }

        if ((flags & (uint)OrgKdePlasmaWindowManagement.State.Active) != 0 &&
            (state & (uint)OrgKdePlasmaWindowManagement.State.Active) != 0)
        {
            model.Request(id, new ToplevelRequest(ToplevelRequestKind.Activate));
        }

        if ((flags & (uint)OrgKdePlasmaWindowManagement.State.Minimized) != 0)
        {
            model.Request(id, new ToplevelRequest(
                (state & (uint)OrgKdePlasmaWindowManagement.State.Minimized) != 0
                    ? ToplevelRequestKind.Minimize
                    : ToplevelRequestKind.Unminimize));
        }

        if ((flags & (uint)OrgKdePlasmaWindowManagement.State.Maximized) != 0)
        {
            model.Request(id, new ToplevelRequest(
                (state & (uint)OrgKdePlasmaWindowManagement.State.Maximized) != 0
                    ? ToplevelRequestKind.Maximize
                    : ToplevelRequestKind.Unmaximize));
        }

        if ((flags & (uint)OrgKdePlasmaWindowManagement.State.Fullscreen) != 0)
        {
            model.Request(id, new ToplevelRequest(
                (state & (uint)OrgKdePlasmaWindowManagement.State.Fullscreen) != 0
                    ? ToplevelRequestKind.Fullscreen
                    : ToplevelRequestKind.Unfullscreen));
        }
    }

    private void MoveTo(string desktopId, ulong toplevelId)
    {
        if (_workspaces is not { } model)
        {
            return;
        }

        foreach (var (workspaceId, handle, _) in _desktops)
        {
            if (handle == desktopId)
            {
                model.Request(workspaceId, new WorkspaceRequest(WorkspaceRequestKind.Move, ToplevelId: toplevelId));
                return;
            }
        }
    }

    private void CreateAndAdopt(ulong toplevelId)
    {
        if (_workspaces is not { } model || _desktops.Count == 0)
        {
            return;
        }

        var groupId = _desktops[0].GroupId;
        if (_windows.TryGetValue(toplevelId, out var tracked) && tracked.Desktop is { } current)
        {
            foreach (var (_, handle, group) in _desktops)
            {
                if (handle == current)
                {
                    groupId = group;
                    break;
                }
            }
        }

        model.Request(groupId, new WorkspaceRequest(WorkspaceRequestKind.Create, ToplevelId: toplevelId));
    }

    private static uint StateBits(ToplevelState state)
    {
        var bits =
            (uint)OrgKdePlasmaWindowManagement.State.Closeable |
            (uint)OrgKdePlasmaWindowManagement.State.Minimizable |
            (uint)OrgKdePlasmaWindowManagement.State.Maximizable |
            (uint)OrgKdePlasmaWindowManagement.State.Fullscreenable |
            (uint)OrgKdePlasmaWindowManagement.State.Movable |
            (uint)OrgKdePlasmaWindowManagement.State.Resizable |
            (uint)OrgKdePlasmaWindowManagement.State.VirtualDesktopChangeable;
        if ((state & ToplevelState.Activated) != 0)
        {
            bits |= (uint)OrgKdePlasmaWindowManagement.State.Active;
        }

        if ((state & ToplevelState.Minimized) != 0)
        {
            bits |= (uint)OrgKdePlasmaWindowManagement.State.Minimized;
        }

        if ((state & ToplevelState.Maximized) != 0)
        {
            bits |= (uint)OrgKdePlasmaWindowManagement.State.Maximized;
        }

        if ((state & ToplevelState.Fullscreen) != 0)
        {
            bits |= (uint)OrgKdePlasmaWindowManagement.State.Fullscreen;
        }

        return bits;
    }
}
