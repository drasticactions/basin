using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class PlasmaWindowManager : IToplevelObserver, IToplevelStackObserver, IWorkspaceObserver, IDisposable
{
    public const int Version = 20;

    private readonly WlGlobal _global;
    private readonly IToplevelModel? _toplevels;
    private readonly IWorkspaceModel? _workspaces;
    private readonly IToplevelStack? _stack;
    private ulong[] _stackScratch = new ulong[16];
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
        public Box SentFrame;
        public Box SentClient;
        public string? SentResourceName;
        public uint SentPid;
        public ulong SentParentId;
        public string SentAppMenuService = "";
        public string SentAppMenuObjectPath = "";
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);

    public PlasmaWindowManager(
        WlServerDisplay display,
        IToplevelModel? toplevels,
        IWorkspaceModel? workspaces,
        IToplevelStack? stack = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _toplevels = toplevels;
        _workspaces = workspaces;
        _stack = stack;
        _global = display.CreateGlobal(OrgKdePlasmaWindowManagement.Interface, Version, OnBind);
        _stack?.AddObserver(this);

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

    public void OnToplevelStackChanged() => OnStackChanged();

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

        _stack?.RemoveObserver(this);
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
                resource.SendStateChanged(StateBits(info.State, resource.Version));
            }
        }

        RefreshGeometry(tracked, info);
        RefreshIdentity(tracked, info);
    }

    private void RefreshIdentity(Tracked tracked, in ToplevelInfo info)
    {
        var resourceName = info.ResourceName.Length > 0 && info.ResourceName != tracked.SentResourceName;
        var pid = info.Pid != 0 && info.Pid != tracked.SentPid;
        var parent = info.ParentId != tracked.SentParentId;
        var appMenu = info.AppMenuService != tracked.SentAppMenuService ||
            info.AppMenuObjectPath != tracked.SentAppMenuObjectPath;
        if (!resourceName && !pid && !parent && !appMenu)
        {
            return;
        }

        foreach (var resource in tracked.Resources)
        {
            if (resource.IsDestroyed)
            {
                continue;
            }

            if (resourceName && resource.SupportsSendResourceNameChanged)
            {
                resource.SendResourceNameChanged(info.ResourceName);
            }

            if (pid)
            {
                resource.SendPidChanged(info.Pid);
            }

            if (parent)
            {
                SendParent(resource, info.ParentId);
            }

            if (appMenu && resource.SupportsSendApplicationMenu)
            {
                resource.SendApplicationMenu(info.AppMenuService, info.AppMenuObjectPath);
            }
        }

        if (resourceName)
        {
            tracked.SentResourceName = info.ResourceName;
        }

        if (pid)
        {
            tracked.SentPid = info.Pid;
        }

        if (parent)
        {
            tracked.SentParentId = info.ParentId;
        }

        if (appMenu)
        {
            tracked.SentAppMenuService = info.AppMenuService;
            tracked.SentAppMenuObjectPath = info.AppMenuObjectPath;
        }
    }

    private void SendParent(OrgKdePlasmaWindowResource resource, ulong parentId)
    {
        if (!resource.SupportsSendParentWindow)
        {
            return;
        }

        OrgKdePlasmaWindowResource? parentResource = null;
        if (parentId != 0)
        {
            if (!_windows.TryGetValue(parentId, out var parent))
            {
                return;
            }

            foreach (var candidate in parent.Resources)
            {
                if (!candidate.IsDestroyed && candidate.Client == resource.Client)
                {
                    parentResource = candidate;
                    break;
                }
            }

            if (parentResource is null)
            {
                return;
            }
        }

        resource.SendParentWindow(parentResource);
    }

    private void RefreshGeometry(Tracked tracked, in ToplevelInfo info)
    {
        var frame = info.Geometry.IsEmpty ? tracked.Box : info.Geometry;
        var client = info.ClientGeometry;
        foreach (var resource in tracked.Resources)
        {
            if (resource.IsDestroyed)
            {
                continue;
            }

            if (!frame.IsEmpty && frame != tracked.SentFrame && resource.SupportsSendGeometry)
            {
                resource.SendGeometry(frame.X, frame.Y, (uint)frame.Width, (uint)frame.Height);
            }

            if (!client.IsEmpty && client != tracked.SentClient && resource.SupportsSendClientGeometry)
            {
                resource.SendClientGeometry(client.X, client.Y, (uint)client.Width, (uint)client.Height);
            }
        }

        if (!frame.IsEmpty)
        {
            tracked.SentFrame = frame;
        }

        if (!client.IsEmpty)
        {
            tracked.SentClient = client;
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

                    if (desktop != tracked.Desktop && resource.SupportsSendVirtualDesktopEntered)
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
            if (apply && _toplevels is { } toplevels && toplevels.TryGet(tracked.Id, out var info))
            {
                RefreshGeometry(tracked, info);
            }
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

        manager.GetStackingOrder += (_, e) =>
        {
            var resource = new OrgKdePlasmaStackingOrderResource(client, manager.Version, e.StackingOrder);
            if (_stack is not null)
            {
                var count = EnumerateStack();
                for (var i = 0; i < count; i++)
                {
                    if (_windows.TryGetValue(_stackScratch[i], out var tracked))
                    {
                        resource.SendWindow(tracked.Uuid);
                    }
                }
            }

            resource.SendDone();
            resource.Destroy();
        };

        manager.SendShowDesktopChanged(0);
        if (_stack is not null && !manager.SupportsSendStackingOrderChanged2)
        {
            SendDeprecatedStackingOrder(manager, EnumerateStack());
        }

        foreach (var tracked in _windows.Values)
        {
            Announce(manager, tracked);
        }
    }

    private void OnStackChanged()
    {
        if (_stack is null || _managers.Count == 0)
        {
            return;
        }

        var count = EnumerateStack();
        foreach (var manager in _managers)
        {
            if (manager.IsDestroyed)
            {
                continue;
            }

            if (manager.SupportsSendStackingOrderChanged2)
            {
                manager.SendStackingOrderChanged2();
            }
            else
            {
                SendDeprecatedStackingOrder(manager, count);
            }
        }
    }

    private int EnumerateStack()
    {
        var count = _stack!.Enumerate(_stackScratch);
        while (count < 0)
        {
            _stackScratch = new ulong[_stackScratch.Length * 2];
            count = _stack.Enumerate(_stackScratch);
        }

        return count;
    }

    private void SendDeprecatedStackingOrder(OrgKdePlasmaWindowManagementResource manager, int count)
    {
        if (!manager.SupportsSendStackingOrderChanged)
        {
            return;
        }

        var tracked = 0;
        for (var i = 0; i < count; i++)
        {
            if (_windows.ContainsKey(_stackScratch[i]))
            {
                tracked++;
            }
        }

        var ids = new byte[tracked * 4];
        var uuids = new string[tracked];
        var next = 0;
        for (var i = 0; i < count; i++)
        {
            if (_windows.TryGetValue(_stackScratch[i], out var window))
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                    ids.AsSpan(next * 4), (uint)window.Id);
                uuids[next] = window.Uuid;
                next++;
            }
        }

        manager.SendStackingOrderChanged(ids);
        if (manager.SupportsSendStackingOrderUuidChanged)
        {
            manager.SendStackingOrderUuidChanged(string.Join(';', uuids));
        }
    }

    private static void Announce(OrgKdePlasmaWindowManagementResource manager, Tracked tracked)
    {
        if (manager.SupportsSendWindowWithUuid)
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
        resource.SendToOutput += (_, e) =>
        {
            if (OutputGlobal.FromResource(e.Output)?.Output is { } output)
            {
                _toplevels?.Request(id, new ToplevelRequest(ToplevelRequestKind.SendToOutput, output));
            }
        };
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
            resource.SendStateChanged(StateBits(info.State, resource.Version));
            var frame = info.Geometry.IsEmpty ? tracked.Box : info.Geometry;
            if (!frame.IsEmpty && resource.SupportsSendGeometry)
            {
                resource.SendGeometry(frame.X, frame.Y, (uint)frame.Width, (uint)frame.Height);
            }

            if (!info.ClientGeometry.IsEmpty && resource.SupportsSendClientGeometry)
            {
                resource.SendClientGeometry(
                    info.ClientGeometry.X,
                    info.ClientGeometry.Y,
                    (uint)info.ClientGeometry.Width,
                    (uint)info.ClientGeometry.Height);
            }

            if (info.ResourceName.Length > 0 && resource.SupportsSendResourceNameChanged)
            {
                resource.SendResourceNameChanged(info.ResourceName);
            }

            if (info.Pid != 0)
            {
                resource.SendPidChanged(info.Pid);
            }

            if (info.ParentId != 0)
            {
                SendParent(resource, info.ParentId);
            }

            if (info.AppMenuService.Length > 0 && resource.SupportsSendApplicationMenu)
            {
                resource.SendApplicationMenu(info.AppMenuService, info.AppMenuObjectPath);
            }
        }

        if (tracked.Desktop is { } desktop && resource.SupportsSendVirtualDesktopEntered)
        {
            resource.SendVirtualDesktopEntered(desktop);
        }

        if (resource.SupportsSendInitialState)
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

        if ((flags & (uint)OrgKdePlasmaWindowManagement.State.NoBorder) != 0)
        {
            model.Request(id, new ToplevelRequest(
                (state & (uint)OrgKdePlasmaWindowManagement.State.NoBorder) != 0
                    ? ToplevelRequestKind.SetNoBorder
                    : ToplevelRequestKind.UnsetNoBorder));
        }

        if ((flags & (uint)OrgKdePlasmaWindowManagement.State.ExcludeFromCapture) != 0)
        {
            model.Request(id, new ToplevelRequest(
                (state & (uint)OrgKdePlasmaWindowManagement.State.ExcludeFromCapture) != 0
                    ? ToplevelRequestKind.ExcludeFromCapture
                    : ToplevelRequestKind.IncludeInCapture));
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

    private static uint StateBits(ToplevelState state, uint version)
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

        if (version >= 2 && (state & ToplevelState.SkipTaskbar) != 0)
        {
            bits |= (uint)OrgKdePlasmaWindowManagement.State.Skiptaskbar;
        }

        if (version >= 9 && (state & ToplevelState.SkipSwitcher) != 0)
        {
            bits |= (uint)OrgKdePlasmaWindowManagement.State.Skipswitcher;
        }

        if (version >= 19)
        {
            if ((state & ToplevelState.NoBorder) != 0)
            {
                bits |= (uint)OrgKdePlasmaWindowManagement.State.NoBorder;
            }

            if ((state & ToplevelState.CanSetNoBorder) != 0)
            {
                bits |= (uint)OrgKdePlasmaWindowManagement.State.CanSetNoBorder;
            }
        }

        if (version >= 20 && (state & ToplevelState.ExcludedFromCapture) != 0)
        {
            bits |= (uint)OrgKdePlasmaWindowManagement.State.ExcludeFromCapture;
        }

        return bits;
    }
}
