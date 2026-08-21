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
