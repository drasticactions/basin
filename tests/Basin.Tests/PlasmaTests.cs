using Basin.Capabilities;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class PlasmaVirtualDesktopTests
{
    private static Basin.Desktop.Protocol.OrgKdePlasmaVirtualDesktopManagement Bind(CompositorTestHost host)
    {
        Basin.Desktop.Protocol.OrgKdePlasmaVirtualDesktopManagement? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_plasma_virtual_desktop_management")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.OrgKdePlasmaVirtualDesktopManagement>(e.Name, 4);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    [Fact]
    public void Desktops_mirror_the_workspace_model()
    {
        using var host = new CompositorTestHost();
        var model = new TestWorkspaceModel();
        var group = model.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        var first = model.AddWorkspace(group, "one", handle: "ws-1", state: WorkspaceStateFlags.Active);
        var second = model.AddWorkspace(group, "two", handle: "ws-2");
        using var manager = new PlasmaVirtualDesktopManager(host.Display, model);

        var management = Bind(host);
        var created = new List<(string Id, uint Position)>();
        var removed = new List<string>();
        var done = 0;
        management.DesktopCreated += (_, e) => created.Add((e.DesktopId, e.Position));
        management.DesktopRemoved += (_, e) => removed.Add(e.DesktopId);
        management.Done += (_, _) => done++;
        host.PumpUntil(() => done >= 1);
        Assert.Equal([("ws-1", 0u), ("ws-2", 1u)], created);

        var desktop = management.GetVirtualDesktop("ws-1");
        string? desktopId = null;
        string? name = null;
        uint? position = null;
        var activated = 0;
        var deactivated = 0;
        desktop.DesktopId += (_, e) => desktopId = e.DesktopId;
        desktop.Name += (_, e) => name = e.Name;
        desktop.Position += (_, e) => position = e.Index;
        desktop.Activated += (_, _) => activated++;
        desktop.Deactivated += (_, _) => deactivated++;
        host.PumpToClient();
        Assert.Equal("ws-1", desktopId);
        Assert.Equal("one", name);
        Assert.Equal(0u, position);
        Assert.Equal(1, activated);

        model.Rename(first, "renamed");
        host.PumpUntil(() => name == "renamed");

        model.SetState(first, WorkspaceStateFlags.None);
        model.SetState(second, WorkspaceStateFlags.Active);
        host.PumpUntil(() => deactivated == 1);

        var desktopRemoved = false;
        desktop.Removed += (_, _) => desktopRemoved = true;
        model.RemoveWorkspace(first);
        host.PumpUntil(() => removed.Contains("ws-1") && desktopRemoved);
    }

    [Fact]
    public void Desktop_requests_reach_the_model()
    {
        using var host = new CompositorTestHost();
        var model = new TestWorkspaceModel();
        var group = model.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        var workspace = model.AddWorkspace(group, "one", handle: "ws-1");
        using var manager = new PlasmaVirtualDesktopManager(host.Display, model);

        var management = Bind(host);
        var desktop = management.GetVirtualDesktop("ws-1");
        desktop.RequestActivate();
        host.PumpUntil(() => model.Requests.Count == 1);
        Assert.Equal((workspace, new WorkspaceRequest(WorkspaceRequestKind.Activate)), model.Requests[0]);

        management.RequestCreateVirtualDesktop("scratch", 5);
        host.PumpUntil(() => model.Requests.Count == 2);
        Assert.Equal((group, new WorkspaceRequest(WorkspaceRequestKind.Create, "scratch")), model.Requests[1]);

        management.RequestRemoveVirtualDesktop("ws-1");
        host.PumpUntil(() => model.Requests.Count == 3);
        Assert.Equal((workspace, new WorkspaceRequest(WorkspaceRequestKind.Remove)), model.Requests[2]);
    }

    [Fact]
    public void An_empty_model_reports_no_desktops_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        using var manager = new PlasmaVirtualDesktopManager(host.Display, EmptyWorkspaceModel.Instance);

        var management = Bind(host);
        var created = 0;
        var done = false;
        management.DesktopCreated += (_, _) => created++;
        management.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.Equal(0, created);

        management.RequestCreateVirtualDesktop("nope", 0);
        host.PumpToServer();
        host.PumpToClient();
    }
}

public sealed class PlasmaWindowTests
{
    private static (TestToplevelModel Toplevels, TestWorkspaceModel Workspaces, ulong Window, ulong First, ulong Second)
        Populate(CompositorTestHost host)
    {
        var toplevels = new TestToplevelModel();
        var workspaces = new TestWorkspaceModel();
        var group = workspaces.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        var first = workspaces.AddWorkspace(group, "one", handle: "ws-1", state: WorkspaceStateFlags.Active);
        var second = workspaces.AddWorkspace(group, "two", handle: "ws-2");
        var window = toplevels.Add("Terminal", "org.foot");
        workspaces.SetMembers(first, new WorkspaceMember(window, new Box(40, 30, 640, 480)));
        return (toplevels, workspaces, window, first, second);
    }

    private static Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement BindManagement(
        CompositorTestHost host, out List<(uint Id, string Uuid)> announced)
    {
        Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement? proxy = null;
        var windows = new List<(uint, string)>();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_plasma_window_management")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement>(e.Name, 16);
                proxy.WindowWithUuid += (_, we) => windows.Add((we.Id, we.Uuid));
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        announced = windows;
        return proxy!;
    }

    private sealed class BoundManagement
    {
        public Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement Proxy = null!;
        public readonly List<(uint Id, string Uuid)> Announced = [];
        public readonly List<string> UuidOrders = [];
        public readonly List<byte[]> IdOrders = [];
        public int Changed2;
    }

    private static BoundManagement BindAt(CompositorTestHost host, uint version)
    {
        var bound = new BoundManagement();
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_plasma_window_management")
            {
                var proxy = registry.Bind<Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement>(e.Name, version);
                bound.Proxy = proxy;
                proxy.WindowWithUuid += (_, we) => bound.Announced.Add((we.Id, we.Uuid));
                proxy.StackingOrderChanged2 += (_, _) => bound.Changed2++;
                proxy.StackingOrderUuidChanged += (_, se) => bound.UuidOrders.Add(se.Uuids);
                proxy.StackingOrderChanged += (_, se) => bound.IdOrders.Add(se.Ids);
            }
        };
        host.PumpToClient();
        Assert.NotNull(bound.Proxy);
        host.PumpToClient();
        return bound;
    }

    [Fact]
    public void Stacking_order_follows_the_capability()
    {
        using var host = new CompositorTestHost();
        var (toplevels, workspaces, window, _, _) = Populate(host);
        var stack = new TestToplevelStack();
        using var manager = new PlasmaWindowManager(host.Display, toplevels, workspaces, stack);
        var second = toplevels.Add("Editor", "org.vim");

        var bound = BindAt(host, 20);
        stack.SetOrder(window, second);
        host.PumpToClient();

        Assert.True(bound.Changed2 >= 1);
        Assert.Empty(bound.UuidOrders);
        Assert.Empty(bound.IdOrders);
    }

    [Fact]
    public void A_client_below_seventeen_gets_the_deprecated_pair()
    {
        using var host = new CompositorTestHost();
        var (toplevels, workspaces, window, _, _) = Populate(host);
        var stack = new TestToplevelStack();
        using var manager = new PlasmaWindowManager(host.Display, toplevels, workspaces, stack);
        var second = toplevels.Add("Editor", "org.vim");

        var legacy = BindAt(host, 16);
        var modern = BindAt(host, 20);
        stack.SetOrder(second, window);
        host.PumpToClient();

        Assert.Equal($"basin-{second};basin-{window}", legacy.UuidOrders[^1]);
        Assert.Equal(0, legacy.Changed2);
        Assert.True(modern.Changed2 >= 1);
        Assert.Empty(modern.UuidOrders);
        Assert.Empty(modern.IdOrders);
    }

    [Fact]
    public void Get_stacking_order_drains_and_dies()
    {
        using var host = new CompositorTestHost();
        var (toplevels, workspaces, window, _, _) = Populate(host);
        var stack = new TestToplevelStack();
        using var manager = new PlasmaWindowManager(host.Display, toplevels, workspaces, stack);
        var second = toplevels.Add("Editor", "org.vim");
        stack.SetOrder(window, second);

        var bound = BindAt(host, 20);
        var order = bound.Proxy.GetStackingOrder();
        var uuids = new List<string>();
        var done = false;
        order.Window += (_, e) => uuids.Add(e.Uuid);
        order.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.Equal([$"basin-{window}", $"basin-{second}"], uuids);
        order.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Client_geometry_rides_the_model()
    {
        using var host = new CompositorTestHost();
        var toplevels = new TestToplevelModel();
        var window = toplevels.Add("Terminal", "org.foot", geometry: new Box(10, 20, 300, 200));
        using var manager = new PlasmaWindowManager(host.Display, toplevels, null);

        var bound = BindAt(host, 20);
        var resource = bound.Proxy.GetWindowByUuid($"basin-{window}");
        var frames = new List<Box>();
        var clients = new List<Box>();
        resource.Geometry += (_, e) => frames.Add(new Box(e.X, e.Y, (int)e.Width, (int)e.Height));
        resource.ClientGeometry += (_, e) => clients.Add(new Box(e.X, e.Y, (int)e.Width, (int)e.Height));
        host.PumpToClient();

        Assert.Equal(new Box(10, 20, 300, 200), frames[^1]);
        Assert.Empty(clients);

        toplevels.SetClientGeometry(window, new Box(14, 24, 292, 190));
        host.PumpToClient();

        Assert.Equal(new Box(14, 24, 292, 190), clients[^1]);
        Assert.NotEqual(frames[^1], clients[^1]);
    }

    [Fact]
    public void Parent_pid_and_resource_name_reach_the_client()
    {
        using var host = new CompositorTestHost();
        var toplevels = new TestToplevelModel();
        var parent = toplevels.Add("Browser", "org.firefox");
        var child = toplevels.Add("Dialog", "org.firefox");
        using var manager = new PlasmaWindowManager(host.Display, toplevels, null);

        var bound = BindAt(host, 20);
        var parentResource = bound.Proxy.GetWindowByUuid($"basin-{parent}");
        var childResource = bound.Proxy.GetWindowByUuid($"basin-{child}");
        var resourceNames = new List<string>();
        var pids = new List<uint>();
        var parents = new List<bool>();
        childResource.ResourceNameChanged += (_, e) => resourceNames.Add(e.ResourceName);
        childResource.PidChanged += (_, e) => pids.Add(e.Pid);
        childResource.ParentWindow += (_, e) => parents.Add(e.Parent is not null);
        host.PumpToClient();

        toplevels.SetIdentity(child, resourceName: "firefox", pid: 4242, parentId: parent);
        host.PumpToClient();

        Assert.Equal("firefox", resourceNames[^1]);
        Assert.Equal(4242u, pids[^1]);
        Assert.True(parents[^1]);

        toplevels.SetIdentity(child, resourceName: "firefox", pid: 4242, parentId: 0);
        host.PumpToClient();
        Assert.False(parents[^1]);
        _ = parentResource;
    }

    [Fact]
    public void Border_and_capture_bits_round_trip()
    {
        using var host = new CompositorTestHost();
        var toplevels = new TestToplevelModel();
        var window = toplevels.Add("Terminal", "org.foot");
        using var manager = new PlasmaWindowManager(host.Display, toplevels, null);

        var bound = BindAt(host, 20);
        var resource = bound.Proxy.GetWindowByUuid($"basin-{window}");
        var noBorder = (uint)Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement.State.NoBorder;
        var exclude = (uint)Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement.State.ExcludeFromCapture;

        resource.SetState(noBorder, noBorder);
        resource.SetState(noBorder, 0);
        resource.SetState(exclude, exclude);
        resource.SetState(exclude, 0);
        host.PumpUntil(() => toplevels.Requests.Count == 4);

        Assert.Equal(
            [
                (window, ToplevelRequestKind.SetNoBorder),
                (window, ToplevelRequestKind.UnsetNoBorder),
                (window, ToplevelRequestKind.ExcludeFromCapture),
                (window, ToplevelRequestKind.IncludeInCapture),
            ],
            toplevels.Requests);

        var masked = BindAt(host, 18);
        var maskedResource = masked.Proxy.GetWindowByUuid($"basin-{window}");
        var maskedStates = new List<uint>();
        maskedResource.StateChanged += (_, e) => maskedStates.Add(e.Flags);
        var fullStates = new List<uint>();
        resource.StateChanged += (_, e) => fullStates.Add(e.Flags);

        toplevels.SetState(
            window,
            ToplevelState.NoBorder | ToplevelState.CanSetNoBorder | ToplevelState.ExcludedFromCapture);
        host.PumpToClient();

        var canSet = (uint)Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement.State.CanSetNoBorder;
        Assert.Equal(0u, maskedStates[^1] & (noBorder | canSet | exclude));
        Assert.Equal(noBorder | canSet | exclude, fullStates[^1] & (noBorder | canSet | exclude));
    }

    [Fact]
    public void Windows_carry_geometry_and_desktops()
    {
        using var host = new CompositorTestHost();
        var (toplevels, workspaces, window, first, second) = Populate(host);
        using var manager = new PlasmaWindowManager(host.Display, toplevels, workspaces);

        var management = BindManagement(host, out var announced);
        host.PumpUntil(() => announced.Count == 1);
        Assert.Equal($"basin-{window}", announced[0].Uuid);

        var resource = management.GetWindowByUuid(announced[0].Uuid);
        string? title = null;
        string? appId = null;
        uint state = 0;
        var geometry = default(Box);
        var entered = new List<string>();
        var left = new List<string>();
        var initial = false;
        resource.TitleChanged += (_, e) => title = e.Title;
        resource.AppIdChanged += (_, e) => appId = e.AppId;
        resource.StateChanged += (_, e) => state = e.Flags;
        resource.Geometry += (_, e) => geometry = new Box(e.X, e.Y, (int)e.Width, (int)e.Height);
        resource.VirtualDesktopEntered += (_, e) => entered.Add(e.Id);
        resource.VirtualDesktopLeft += (_, e) => left.Add(e.Is);
        resource.InitialState += (_, _) => initial = true;
        host.PumpUntil(() => initial);
        Assert.Equal("Terminal", title);
        Assert.Equal("org.foot", appId);
        Assert.NotEqual(0u, state & (uint)Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement.State.Closeable);
        Assert.Equal(new Box(40, 30, 640, 480), geometry);
        Assert.Equal(["ws-1"], entered);

        workspaces.SetMembers(first, new WorkspaceMember(window, new Box(100, 90, 640, 480)));
        host.PumpUntil(() => geometry.X == 100 && geometry.Y == 90);

        workspaces.SetMembers(first);
        workspaces.SetMembers(second, new WorkspaceMember(window, new Box(100, 90, 640, 480)));
        host.PumpUntil(() => entered.Count == 2 && left.Count == 1);
        Assert.Equal("ws-1", left[0]);
        Assert.Equal("ws-2", entered[1]);
    }

    [Fact]
    public void Window_requests_route_to_both_models()
    {
        using var host = new CompositorTestHost();
        var (toplevels, workspaces, window, _, second) = Populate(host);
        using var manager = new PlasmaWindowManager(host.Display, toplevels, workspaces);

        var management = BindManagement(host, out var announced);
        host.PumpUntil(() => announced.Count == 1);
        var resource = management.GetWindowByUuid(announced[0].Uuid);
        host.PumpToClient();

        resource.RequestEnterVirtualDesktop("ws-2");
        host.PumpUntil(() => workspaces.Requests.Count == 1);
        Assert.Equal(
            (second, new WorkspaceRequest(WorkspaceRequestKind.Move, ToplevelId: window)),
            workspaces.Requests[0]);

        resource.Close();
        host.PumpUntil(() => toplevels.Requests.Count == 1);
        Assert.Equal((window, ToplevelRequestKind.Close), toplevels.Requests[0]);

        resource.SetState(
            (uint)Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement.State.Minimized,
            (uint)Basin.Desktop.Protocol.OrgKdePlasmaWindowManagement.State.Minimized);
        host.PumpUntil(() => toplevels.Requests.Count == 2);
        Assert.Equal((window, ToplevelRequestKind.Minimize), toplevels.Requests[1]);
    }

    [Fact]
    public void A_new_desktop_request_creates_with_the_window()
    {
        using var host = new CompositorTestHost();
        var (toplevels, workspaces, window, _, _) = Populate(host);
        using var manager = new PlasmaWindowManager(host.Display, toplevels, workspaces);

        var management = BindManagement(host, out var announced);
        host.PumpUntil(() => announced.Count == 1);
        var resource = management.GetWindowByUuid(announced[0].Uuid);
        host.PumpToClient();

        resource.RequestEnterNewVirtualDesktop();
        host.PumpUntil(() => workspaces.Requests.Count == 1);
        Assert.Equal(
            (1UL, new WorkspaceRequest(WorkspaceRequestKind.Create, ToplevelId: window)),
            workspaces.Requests[0]);
    }

    [Fact]
    public void A_window_gone_from_the_model_unmaps()
    {
        using var host = new CompositorTestHost();
        var (toplevels, workspaces, window, _, _) = Populate(host);
        using var manager = new PlasmaWindowManager(host.Display, toplevels, workspaces);

        var management = BindManagement(host, out var announced);
        host.PumpUntil(() => announced.Count == 1);
        var resource = management.GetWindowByUuid(announced[0].Uuid);
        var unmapped = false;
        resource.Unmapped += (_, _) => unmapped = true;
        host.PumpToClient();

        toplevels.Remove(window);
        host.PumpUntil(() => unmapped);
    }

    [Fact]
    public void Absent_models_announce_nothing_and_the_client_survives()
    {
        using var host = new CompositorTestHost();
        using var manager = new PlasmaWindowManager(host.Display, null, null);

        var management = BindManagement(host, out var announced);
        var resource = management.GetWindowByUuid("no-such-window");
        var unmapped = false;
        resource.Unmapped += (_, _) => unmapped = true;
        host.PumpUntil(() => unmapped);
        Assert.Empty(announced);
    }
}
