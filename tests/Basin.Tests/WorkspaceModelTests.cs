using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class WorkspaceModelTests
{
    private sealed class Bound
    {
        public Basin.Desktop.Protocol.ExtWorkspaceManagerV1 Manager = null!;
        public readonly List<Basin.Desktop.Protocol.ExtWorkspaceGroupHandleV1> Groups = [];
        public readonly List<Basin.Desktop.Protocol.ExtWorkspaceHandleV1> Workspaces = [];
        public readonly List<string> Names = [];
        public readonly List<uint> States = [];
        public readonly List<uint[]> Coordinates = [];
        public readonly List<(Basin.Desktop.Protocol.ExtWorkspaceGroupHandleV1 Group, int Delta)> OutputEvents = [];
        public readonly List<(Basin.Desktop.Protocol.ExtWorkspaceGroupHandleV1 Group, int Delta)> MemberEvents = [];
        public int GroupsRemoved;
        public int WorkspacesRemoved;
        public int Done;
    }

    private static Bound BindClient(CompositorTestHost host, ShmTestClient? client = null)
    {
        var bound = new Bound();
        var registry = (client ?? host.Client).Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_workspace_manager_v1")
            {
                bound.Manager = registry.Bind<Basin.Desktop.Protocol.ExtWorkspaceManagerV1>(e.Name, 1);
                bound.Manager.WorkspaceGroup += (_, ge) =>
                {
                    var group = ge.WorkspaceGroup;
                    bound.Groups.Add(group);
                    group.OutputEnter += (_, _) => bound.OutputEvents.Add((group, 1));
                    group.OutputLeave += (_, _) => bound.OutputEvents.Add((group, -1));
                    group.WorkspaceEnter += (_, _) => bound.MemberEvents.Add((group, 1));
                    group.WorkspaceLeave += (_, _) => bound.MemberEvents.Add((group, -1));
                    group.Removed += (_, _) => bound.GroupsRemoved++;
                };
                bound.Manager.Workspace += (_, we) =>
                {
                    var workspace = we.Workspace;
                    bound.Workspaces.Add(workspace);
                    workspace.Name += (_, ne) => bound.Names.Add(ne.Name);
                    workspace.StateEvent += (_, se) => bound.States.Add((uint)se.State);
                    workspace.Coordinates += (_, ce) =>
                        bound.Coordinates.Add(MemoryMarshal.Cast<byte, uint>(ce.Coordinates).ToArray());
                    workspace.Removed += (_, _) => bound.WorkspacesRemoved++;
                };
                bound.Manager.Done += (_, _) => bound.Done++;
            }
        };
        host.PumpToClient();
        Assert.NotNull(bound.Manager);
        return bound;
    }

    [Fact]
    public void Projection_announces_and_tracks_model_mutations()
    {
        using var host = new CompositorTestHost();
        var model = new TestWorkspaceModel();
        var group = model.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        var first = model.AddWorkspace(group, "one", handle: "ws-1", coordinates: [0]);
        using var manager = new WorkspaceManager(host.Display, model);

        var bound = BindClient(host);
        host.PumpUntil(() => bound.Done >= 1);

        Assert.Single(bound.Groups);
        Assert.Single(bound.Workspaces);
        Assert.Equal("one", Assert.Single(bound.Names));
        Assert.Equal([0u], Assert.Single(bound.Coordinates));
        Assert.Contains((bound.Groups[0], 1), bound.OutputEvents);
        Assert.Contains((bound.Groups[0], 1), bound.MemberEvents);

        model.Rename(first, "renamed");
        host.PumpUntil(() => bound.Names.Contains("renamed"));

        model.SetState(first, WorkspaceStateFlags.Active);
        host.PumpUntil(() => bound.States.Contains(1u));

        var second = model.AddWorkspace(group, "two", coordinates: [1]);
        host.PumpUntil(() => bound.Workspaces.Count == 2);
        Assert.Contains(bound.Coordinates, c => c is [1u]);

        model.RemoveWorkspace(second);
        host.PumpUntil(() => bound.WorkspacesRemoved == 1);

        model.RemoveGroup(group);
        host.PumpUntil(() => bound.GroupsRemoved == 1 && bound.WorkspacesRemoved == 2);
    }

    [Fact]
    public void Create_request_reaches_the_model_with_its_name()
    {
        using var host = new CompositorTestHost();
        var model = new TestWorkspaceModel();
        var group = model.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        using var manager = new WorkspaceManager(host.Display, model);

        var bound = BindClient(host);
        host.PumpUntil(() => bound.Done >= 1);

        bound.Groups[0].CreateWorkspace("scratch");
        host.PumpToServer();
        Assert.Empty(model.Requests);

        bound.Manager.Commit();
        host.PumpUntil(() => model.Requests.Count == 1);
        var (targetId, request) = model.Requests[0];
        Assert.Equal(group, targetId);
        Assert.Equal(WorkspaceRequestKind.Create, request.Kind);
        Assert.Equal("scratch", request.Name);
    }

    [Fact]
    public void Assign_request_reaches_the_model_with_the_target_group()
    {
        using var host = new CompositorTestHost();
        var model = new TestWorkspaceModel();
        var left = model.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        var right = model.AddGroup(clientsCanCreateWorkspaces: true);
        var workspace = model.AddWorkspace(left, "one");
        using var manager = new WorkspaceManager(host.Display, model);

        var bound = BindClient(host);
        host.PumpUntil(() => bound.Done >= 1 && bound.Groups.Count == 2);

        bound.Workspaces[0].Assign(bound.Groups[1]);
        bound.Manager.Commit();
        host.PumpUntil(() => model.Requests.Count == 1);
        var (targetId, request) = model.Requests[0];
        Assert.Equal(workspace, targetId);
        Assert.Equal(WorkspaceRequestKind.Assign, request.Kind);
        Assert.Equal(right, request.GroupId);
    }

    [Fact]
    public void Model_group_move_emits_leave_then_enter()
    {
        using var host = new CompositorTestHost();
        var model = new TestWorkspaceModel();
        var left = model.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        var right = model.AddGroup(clientsCanCreateWorkspaces: true);
        var workspace = model.AddWorkspace(left, "one");
        using var manager = new WorkspaceManager(host.Display, model);

        var bound = BindClient(host);
        host.PumpUntil(() => bound.Done >= 1 && bound.Groups.Count == 2);
        Assert.Equal([(bound.Groups[0], 1)], bound.MemberEvents);

        model.MoveToGroup(workspace, right);
        host.PumpUntil(() => bound.MemberEvents.Count == 3);
        Assert.Equal((bound.Groups[0], -1), bound.MemberEvents[1]);
        Assert.Equal((bound.Groups[1], 1), bound.MemberEvents[2]);
    }

    [Fact]
    public void Output_changes_project_after_first_sight()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        using var secondGlobal = new OutputGlobal(host.Display, second);

        var model = new TestWorkspaceModel();
        var group = model.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        model.AddWorkspace(group, "one");
        using var manager = new WorkspaceManager(host.Display, model);

        var client = host.ConnectClient();
        var bound = BindClient(host, client);
        host.PumpUntil(() => bound.Done >= 1);
        Assert.Equal(1, bound.OutputEvents.Count(e => e.Delta == 1));

        model.SetOutputs(group, second);
        host.PumpUntil(() =>
            bound.OutputEvents.Count(e => e.Delta == -1) == 1 &&
            bound.OutputEvents.Count(e => e.Delta == 1) == 2);

        host.DisconnectClient(client);
        host.PumpToServer();
    }

    [Fact]
    public void Coordinates_diff_by_sequence_not_reference()
    {
        using var host = new CompositorTestHost();
        var model = new TestWorkspaceModel();
        var group = model.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        var plain = model.AddWorkspace(group, "plain");
        var placed = model.AddWorkspace(group, "placed", coordinates: [7]);
        using var manager = new WorkspaceManager(host.Display, model);

        var bound = BindClient(host);
        host.PumpUntil(() => bound.Done >= 1);
        Assert.Equal([7u], Assert.Single(bound.Coordinates));

        model.SetCoordinates(placed, [8]);
        host.PumpUntil(() => bound.Coordinates.Count == 2);
        Assert.Equal([8u], bound.Coordinates[1]);

        model.SetCoordinates(placed, [8]);
        model.Raise();
        host.PumpUntil(() => bound.Done >= 4);
        Assert.Equal(2, bound.Coordinates.Count);

        model.SetCoordinates(placed, []);
        host.PumpUntil(() => bound.Coordinates.Count == 3);
        Assert.Empty(bound.Coordinates[2]);

        model.SetCoordinates(plain, null);
        model.Raise();
        host.PumpUntil(() => bound.Done >= 7);
        Assert.Equal(3, bound.Coordinates.Count);
    }

    [Fact]
    public void Requests_round_trip_and_refusal_changes_nothing()
    {
        using var host = new CompositorTestHost();
        var model = new TestWorkspaceModel();
        var group = model.AddGroup(clientsCanCreateWorkspaces: true, host.Output);
        var workspace = model.AddWorkspace(group, "one");
        using var manager = new WorkspaceManager(host.Display, model);

        var bound = BindClient(host);
        host.PumpUntil(() => bound.Done >= 1);

        bound.Workspaces[0].Activate();
        bound.Manager.Commit();
        host.PumpUntil(() => model.Requests.Count == 1);
        Assert.Equal((workspace, new WorkspaceRequest(WorkspaceRequestKind.Activate)), model.Requests[0]);

        model.SetState(workspace, WorkspaceStateFlags.Active);
        host.PumpUntil(() => bound.States.Contains(1u));

        model.Accept = false;
        bound.Workspaces[0].Deactivate();
        bound.Manager.Commit();
        host.PumpUntil(() => model.Requests.Count == 2);
        Assert.Equal(WorkspaceRequestKind.Deactivate, model.Requests[1].Request.Kind);
        Assert.Equal(1u, bound.States.Max());

        host.PumpToServer();
        host.PumpToClient();
    }
}
