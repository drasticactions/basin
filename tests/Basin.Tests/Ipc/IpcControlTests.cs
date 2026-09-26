using System.Text.Json;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Ipc;
using Basin.Shell.Xdg;
using Xunit;

namespace Basin.Tests;

public sealed class IpcControlTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static string? ErrorCode(string frame) =>
        Parse(frame).TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    [Fact]
    public void Window_requests_reach_the_model()
    {
        var model = new TestToplevelModel();
        using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        var id = model.Add("t", "a", geometry: new Box(5, 6, 70, 80));
        var peer = rig.Connect();

        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/activate","params":{"id":{{{id}}}}}""")));
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/set-state","params":{"id":{{{id}}},"maximized":true,"fullscreen":false}}""")));
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/move","params":{"id":{{{id}}},"x":100,"y":200}}""")));
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/resize","params":{"id":{{{id}}},"width":300,"height":150}}""")));
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/close","params":{"id":{{{id}}}}}""")));
        Assert.Equal(
            [ToplevelRequestKind.Activate, ToplevelRequestKind.Maximize, ToplevelRequestKind.Unfullscreen,
             ToplevelRequestKind.Move, ToplevelRequestKind.Resize, ToplevelRequestKind.Close],
            model.Requests.Select(r => r.Kind).ToArray());
        Assert.Equal(new Box(100, 200, 70, 80), model.RequestLog[3].Request.Geometry);
        Assert.Equal(new Box(5, 6, 300, 150), model.RequestLog[4].Request.Geometry);

        Assert.Equal(IpcErrorCodes.NotFound, ErrorCode(peer.Call("""{"method":"windows/close","params":{"id":42}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call($$$"""{"method":"windows/set-state","params":{"id":{{{id}}}}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call($$$"""{"method":"windows/resize","params":{"id":{{{id}}},"width":0,"height":1}}""")));
        model.Refuse = true;
        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call($$$"""{"method":"windows/activate","params":{"id":{{{id}}}}}""")));
    }

    [Fact]
    public void Move_on_an_xdg_source_is_refused_until_the_compositor_subscribes()
    {
        var model = new AggregateToplevelModel();
        using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        using var source = new XdgToplevelSource(rig.Host.Shell);
        model.Add(source);
        _ = MappedToplevel.Map(rig.Host, rig.Host.Client);
        var peer = rig.Connect();
        var id = Parse(peer.Call("""{"method":"windows/list"}""")).GetProperty("result").GetProperty("windows")[0]
            .GetProperty("id").GetUInt64();

        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call($$$"""{"method":"windows/move","params":{"id":{{{id}}},"x":1,"y":2}}""")));
        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call($$$"""{"method":"windows/resize","params":{"id":{{{id}}},"width":10,"height":20}}""")));

        Box? moved = null;
        Box? resized = null;
        source.MoveRequested += (_, box) => moved = box;
        source.ResizeRequested += (_, box) => resized = box;
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/move","params":{"id":{{{id}}},"x":1,"y":2}}""")));
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/resize","params":{"id":{{{id}}},"width":10,"height":20}}""")));
        Assert.Equal(1, moved!.Value.X);
        Assert.Equal(2, moved.Value.Y);
        Assert.Equal(10, resized!.Value.Width);
        Assert.Equal(20, resized.Value.Height);
    }

    [Fact]
    public void Wait_is_overtaken_and_then_answers_when_the_window_maps()
    {
        var model = new TestToplevelModel();
        using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        var peer = rig.Connect();
        peer.Send("""{"id":"wait","method":"windows/wait","params":{"app_id":"late.app","timeout_ms":5000}}""");
        peer.Send("""{"id":"now","method":"ipc/version"}""");
        Assert.Equal("now", Parse(peer.Receive()).GetProperty("id").GetString());
        Assert.Equal(1, model.ObserverCount);

        _ = model.Add("other", "other.app");
        rig.Pump();
        Assert.False(peer.HasFrame());
        var id = model.Add("late", "late.app");
        var reply = Parse(peer.Receive());
        Assert.Equal("wait", reply.GetProperty("id").GetString());
        Assert.Equal(id, reply.GetProperty("result").GetProperty("id").GetUInt64());
        Assert.Equal(0, model.ObserverCount);

        var existing = Parse(peer.Call("""{"method":"windows/wait","params":{"title":"lat"}}"""));
        Assert.Equal(id, existing.GetProperty("result").GetProperty("id").GetUInt64());
    }

    [Fact]
    public void Wait_fails_at_its_timeout_and_teardown_releases_a_pending_one()
    {
        var model = new TestToplevelModel();
        var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        var peer = rig.Connect();
        peer.Send("""{"id":1,"method":"windows/wait","params":{"app_id":"never","timeout_ms":20}}""");
        var deadline = Environment.TickCount64 + 2000;
        while (!peer.HasFrame() && Environment.TickCount64 < deadline)
        {
            rig.Host.Loop.Dispatch(10);
        }

        Assert.Equal(IpcErrorCodes.Failed, ErrorCode(peer.Receive()));
        Assert.Equal(0, model.ObserverCount);

        peer.Send("""{"id":2,"method":"windows/wait","params":{"app_id":"never","timeout_ms":60000}}""");
        for (var i = 0; i < 10 && model.ObserverCount == 0; i++)
        {
            rig.Pump();
        }

        Assert.Equal(1, model.ObserverCount);
        rig.Dispose();
        Assert.Equal(0, model.ObserverCount);
    }

    [Fact]
    public void Workspace_requests_and_send_to_workspace()
    {
        var model = new TestToplevelModel();
        var workspaces = new TestWorkspaceModel();
        using var rig = new IpcTestRig(services =>
        {
            services.Use<IToplevelModel>(model);
            services.Use<IWorkspaceModel>(workspaces);
        });
        var group = workspaces.AddGroup();
        var one = workspaces.AddWorkspace(group, "one", state: WorkspaceStateFlags.Active);
        var id = model.Add("t", "a");
        var peer = rig.Connect();
        var groups = Parse(peer.Call("""{"method":"workspaces/list"}""")).GetProperty("result").GetProperty("groups");
        Assert.Equal("one", groups[0].GetProperty("workspaces")[0].GetProperty("name").GetString());

        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"workspaces/activate","params":{"id":{{{one}}}}}""")));
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"workspaces/create","params":{"group":{{{group}}},"name":"two"}}""")));
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/send-to-workspace","params":{"id":{{{id}}},"workspace":{{{one}}}}}""")));
        Assert.Equal(
            [WorkspaceRequestKind.Activate, WorkspaceRequestKind.Create, WorkspaceRequestKind.Assign],
            workspaces.Requests.Select(r => r.Request.Kind).ToArray());
        Assert.Equal(id, workspaces.Requests[2].Request.ToplevelId);
        workspaces.Accept = false;
        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call($$$"""{"method":"workspaces/remove","params":{"id":{{{one}}}}}""")));
    }

    [Fact]
    public void Outputs_apply_power_and_their_errors()
    {
        var power = new TestOutputPower();
        using var rig = new IpcTestRig(services =>
        {
            var layout = new OutputLayout();
            services.Use(layout);
            services.Use<IOutputConfiguration>(new LayoutOutputConfiguration(layout));
            services.Use<IOutputPower>(power);
        });
        var layout = rig.Services.Require<OutputLayout>();
        layout.Add(rig.Host.Output, 0, 0);
        var name = rig.Host.Output.Name;
        var peer = rig.Connect();

        var tested = Parse(peer.Call($$$"""{"method":"outputs/test","params":{"entries":[{"name":"{{{name}}}","scale":2}]}}"""));
        Assert.True(tested.GetProperty("result").GetProperty("ok").GetBoolean());
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"outputs/apply","params":{"entries":[{"name":"{{{name}}}","scale":2,"position":{"x":10,"y":0}}]}}""")));
        Assert.Equal(2, rig.Host.Output.Scale);
        Assert.Equal(10, layout.BoxOf(rig.Host.Output).X);

        Assert.Equal(IpcErrorCodes.NotFound, ErrorCode(peer.Call("""{"method":"outputs/apply","params":{"entries":[{"name":"NOPE"}]}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call($$$"""{"method":"outputs/apply","params":{"entries":[{"name":"{{{name}}}","transform":"sideways"}]}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"outputs/apply","params":{"entries":{}}}""")));

        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"outputs/power","params":{"output":"{{{name}}}","on":false}}""")));
        Assert.False(power.Requests[0].On);
        var listed = Parse(peer.Call("""{"method":"outputs/list"}""")).GetProperty("result").GetProperty("outputs")[0];
        Assert.False(listed.GetProperty("power").GetBoolean());
    }

    [Fact]
    public void An_id_can_be_a_string_of_its_decimal_digits()
    {
        var model = new TestToplevelModel { IdBase = 1UL << 56 };
        using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        var id = model.Add("big", "big.app");
        var peer = rig.Connect();

        var got = Parse(peer.Call($$$"""{"method":"windows/get","params":{"id":"{{{id}}}"}}"""));
        Assert.Equal(id, got.GetProperty("result").GetProperty("id").GetUInt64());
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"windows/activate","params":{"id":"{{{id}}}"}}""")));
        Assert.Contains((id, ToplevelRequestKind.Activate), model.Requests);

        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"windows/get","params":{"id":"-1"}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"windows/get","params":{"id":"0x10"}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"windows/get","params":{"id":"99999999999999999999999"}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"windows/get","params":{"id":""}}""")));
    }
}
