using System.Text.Json;
using Basin.Capabilities;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcDiagnosticsTests
{
    [Fact]
    public void The_diag_group_registers_with_the_capability_and_answers()
    {
        var diagnostics = new IpcFullRig.QuietDiagnostics();
        using var rig = IpcFullRig.Create(services: services => services.Use<ISceneDiagnostics>(diagnostics));
        var peer = rig.Connect();

        using var tree = JsonDocument.Parse(peer.Call("""{"method":"diag/tree","params":{}}"""));
        var nodes = tree.RootElement.GetProperty("result").GetProperty("nodes");
        Assert.Equal(2, nodes.GetArrayLength());
        Assert.Equal("tree/probe", nodes[1].GetProperty("path").GetString());
        Assert.Equal(10, nodes[1].GetProperty("width").GetInt32());
        Assert.False(nodes[1].GetProperty("enabled").GetBoolean());

        using var frames = JsonDocument.Parse(peer.Call("""{"method":"diag/frames","params":{}}"""));
        var frame = frames.RootElement.GetProperty("result").GetProperty("frames")[0];
        Assert.Equal("close", frame.GetProperty("hot").GetString());
        Assert.Equal(712, frame.GetProperty("outer").GetProperty("width").GetInt32());
        Assert.Equal(0, frames.RootElement.GetProperty("result").GetProperty("clock_sinks").GetInt32());

        using var recording = JsonDocument.Parse(peer.Call(
            """{"method":"diag/record","params":{"x":0,"y":0,"width":40,"height":30,"frames":2,"path":"/tmp/never.png","max_dimension":20}}"""));
        var result = recording.RootElement.GetProperty("result");
        Assert.Equal(7, result.GetProperty("delta_max").GetInt32());
        Assert.Equal(1, result.GetProperty("per_frame")[1].GetProperty("planes").GetArrayLength());
        Assert.Equal(20, diagnostics.LastRecording!.MaxDimension);
        Assert.Equal(new Box(0, 0, 40, 30), diagnostics.LastRecording.Region);

        using var bad = JsonDocument.Parse(peer.Call("""{"method":"diag/damage-trace","params":{"mode":"loud"}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, bad.RootElement.GetProperty("error").GetProperty("code").GetString());

        _ = peer.Call("""{"method":"diag/damage-trace","params":{"mode":"stack"}}""");
        _ = peer.Call("""{"method":"diag/frame-trace","params":{"on":true}}""");
        Assert.True(Assert.Single(diagnostics.DamageWatchers).Stacks);
        Assert.Single(diagnostics.FrameWatchers);
        diagnostics.DamageWatchers[0].Sink(new DamageInfo("tree/probe", new Box(1, 2, 3, 4), "at somewhere"));
        using (var damage = JsonDocument.Parse(peer.Receive()))
        {
            Assert.Equal("diag/damage", damage.RootElement.GetProperty("event").GetString());
            Assert.Equal("at somewhere", damage.RootElement.GetProperty("data").GetProperty("stack").GetString());
        }

        _ = peer.Call("""{"method":"diag/damage-trace","params":{"mode":"off"}}""");
        _ = peer.Call("""{"method":"diag/frame-trace","params":{"on":false}}""");
        Assert.Empty(diagnostics.DamageWatchers);
        Assert.Empty(diagnostics.FrameWatchers);
    }

    [Fact]
    public void A_window_named_by_app_id_title_or_id_has_parts_a_click_a_drag_and_planes()
    {
        var diagnostics = new IpcFullRig.QuietDiagnostics();
        using var rig = IpcFullRig.Create(services: services => services.Use<ISceneDiagnostics>(diagnostics));
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        var window = MappedToplevel.Map(rig.Host, rig.Host.Client, 60, 50, 0xFF0000FF);
        var id = model.Add("a terminal", "foot", window.ServerSurface, new Box(0, 0, 60, 50));
        var peer = rig.Connect();

        foreach (var name in new[] { "\"foot\"", "\"terminal\"", $"\"{id}\"", $"{id}" })
        {
            using var parts = JsonDocument.Parse(peer.Call($$$"""{"method":"diag/parts","params":{"window":{{{name}}}}}"""));
            var result = parts.RootElement.GetProperty("result");
            Assert.Equal(id, result.GetProperty("window").GetUInt64());
            Assert.Equal("a terminal", result.GetProperty("title").GetString());
            var found = result.GetProperty("parts").EnumerateArray()
                .Select(part => $"{part.GetProperty("part").GetString()} {part.GetProperty("box").GetProperty("x").GetInt32()}").ToArray();
            Assert.Equal(["title 10", "close 90"], found);
        }

        using var missing = JsonDocument.Parse(peer.Call("""{"method":"diag/parts","params":{"window":"nothing"}}"""));
        Assert.Equal(IpcErrorCodes.NotFound, missing.RootElement.GetProperty("error").GetProperty("code").GetString());
        using var badPart = JsonDocument.Parse(peer.Call("""{"method":"diag/click","params":{"window":"foot","part":"wing"}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, badPart.RootElement.GetProperty("error").GetProperty("code").GetString());

        var synthetic = (IpcInputTests.RecordingSynthetic)rig.Server.SyntheticInput!;
        _ = peer.Call("""{"method":"diag/click","params":{"window":"foot","part":"close"}}""");
        Assert.Equal(["move 100 15", "button 272 True", "button 272 False"], synthetic.Log);

        synthetic.Log.Clear();
        using var drag = JsonDocument.Parse(peer.Call("""{"method":"diag/drag","params":{"window":"foot","dx":40,"dy":-10,"steps":2,"interval_ms":1}}"""));
        Assert.Equal(100, drag.RootElement.GetProperty("result").GetProperty("to_x").GetDouble());
        Assert.Equal("title", drag.RootElement.GetProperty("result").GetProperty("part").GetString());
        Assert.Equal(["move 60 15", "button 272 True", "move 80 10", "move 100 5", "button 272 False"], synthetic.Log);

        _ = peer.Call("""{"method":"diag/planes","params":{"on":false}}""");
        Assert.False(diagnostics.PlanesAllowed);
    }

    [Fact]
    public void Window_methods_and_input_take_an_app_id_or_a_title()
    {
        using var rig = IpcFullRig.Create();
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        var id = model.Add("a terminal", "foot", geometry: new Box(20, 10, 60, 50));
        ((TestToplevelStack)rig.Services.Require<IToplevelStack>()).SetOrder(id);
        var peer = rig.Connect();

        _ = peer.Call("""{"method":"windows/move","params":{"id":"foot","x":40,"y":30}}""");
        _ = peer.Call("""{"method":"windows/activate","params":{"id":"terminal"}}""");
        Assert.Equal([(id, ToplevelRequestKind.Move), (id, ToplevelRequestKind.Activate)], model.Requests);

        using var get = JsonDocument.Parse(peer.Call("""{"method":"windows/get","params":{"id":"foot"}}"""));
        Assert.Equal("foot", get.RootElement.GetProperty("result").GetProperty("app_id").GetString());
        using var missing = JsonDocument.Parse(peer.Call("""{"method":"windows/close","params":{"id":"nothing"}}"""));
        Assert.Equal(IpcErrorCodes.NotFound, missing.RootElement.GetProperty("error").GetProperty("code").GetString());

        var synthetic = (IpcInputTests.RecordingSynthetic)rig.Server.SyntheticInput!;
        synthetic.Log.Clear();
        using var move = JsonDocument.Parse(peer.Call("""{"method":"input/pointer-move","params":{"window":"foot","x":5,"y":6}}"""));
        Assert.False(move.RootElement.TryGetProperty("error", out var error), error.ToString());
        Assert.Equal(["move 25 16"], synthetic.Log);
    }

    [Fact]
    public void A_name_that_matches_several_windows_picks_the_active_one_or_lists_them()
    {
        using var rig = IpcFullRig.Create();
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        var first = model.Add("one", "foot", geometry: new Box(0, 0, 60, 50));
        var second = model.Add("two", "foot", geometry: new Box(0, 0, 60, 50));
        var peer = rig.Connect();

        using var refused = JsonDocument.Parse(peer.Call("""{"method":"windows/get","params":{"id":"foot"}}"""));
        var message = refused.RootElement.GetProperty("error").GetProperty("message").GetString()!;
        Assert.Contains($"{first} (app_id=foot, title=one)", message, StringComparison.Ordinal);
        Assert.Contains($"{second} (app_id=foot, title=two)", message, StringComparison.Ordinal);

        model.SetState(second, ToplevelState.Activated);
        using var picked = JsonDocument.Parse(peer.Call("""{"method":"windows/get","params":{"id":"foot"}}"""));
        Assert.Equal(second, picked.RootElement.GetProperty("result").GetProperty("id").GetUInt64());
        using var exact = JsonDocument.Parse(peer.Call("""{"method":"windows/get","params":{"id":"one"}}"""));
        Assert.Equal(first, exact.RootElement.GetProperty("result").GetProperty("id").GetUInt64());
    }

    [Fact]
    public void The_stdin_words_render_the_typed_replies_as_lines()
    {
        var diagnostics = new IpcFullRig.QuietDiagnostics();
        using var rig = new IpcTestRig(services => services.Use<ISceneDiagnostics>(diagnostics));
        new IpcLineReport().AddDiagnosticsLines(rig.Server.Methods);
        rig.Server.Start();
        rig.Server.StartLineFront(-1);
        var previous = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            rig.Server.LineFront!.Execute("scene-tree");
            rig.Server.LineFront.Execute("frames");
            rig.Server.LineFront.Execute("record 0 0 40 30 2 /tmp/never.png");
            rig.Server.LineFront.Execute("frame-trace on");
            rig.Server.LineFront.Execute("damage-trace stack");
            diagnostics.FrameWatchers[0](diagnostics.Frames()[0]);
            diagnostics.DamageWatchers[0].Sink(new DamageInfo("tree/probe", new Box(1, 2, 3, 4), "at somewhere"));
            rig.Server.LineFront.Execute("frame-trace off");
        }
        finally
        {
            Console.SetOut(previous);
        }

        var lines = captured.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("TREE tree at=0,0", lines[0]);
        Assert.Equal("TREE   rect \"probe\" at=4,5 disabled size=10x6", lines[1]);
        Assert.StartsWith("FRAME probe draws=3 animating=False visible=True outer=-6,-36,712x529 active=True", lines[2], StringComparison.Ordinal);
        Assert.Contains("hot=close", lines[2], StringComparison.Ordinal);
        Assert.Equal("CLOCK sinks=0", lines[3]);
        Assert.Equal("RECORD-FRAME 0 at=1,2 luma=10.0 peak=100.0 delta=0 planes=0", lines[4]);
        Assert.Equal("RECORD-FRAME 1 at=3,2 luma=12.0 peak=140.0 delta=7 planes=1 plane=0,0,40x30", lines[5]);
        Assert.Equal("RECORD /tmp/never.png frames=2 size=40x30 luma=10.0..12.0 peak=100.0..140.0 delta-max=7", lines[6]);
        Assert.StartsWith("FRAMEDRAW probe draws=3 ", lines[7], StringComparison.Ordinal);
        Assert.Equal(["DAMAGE tree/probe 1,2,3x4", "DAMAGE-STACK tree/probe", "at somewhere"], lines[8..11]);
        Assert.Empty(diagnostics.FrameWatchers);
    }

    [Fact]
    public void A_recording_replies_when_it_finishes()
    {
        var diagnostics = new IpcFullRig.QuietDiagnostics { DeferRecordings = true };
        using var rig = IpcFullRig.Create(services: services => services.Use<ISceneDiagnostics>(diagnostics));
        var peer = rig.Connect();
        peer.Send("""{"id":7,"method":"diag/record","params":{"x":0,"y":0,"width":40,"height":30,"frames":2,"path":"/tmp/never.png"}}""");
        rig.Pump();
        Assert.NotNull(diagnostics.PendingRecording);
        diagnostics.PendingRecording!(IpcFullRig.QuietDiagnostics.Result(diagnostics.LastRecording!));
        using var reply = JsonDocument.Parse(peer.Receive());
        Assert.Equal(2, reply.RootElement.GetProperty("result").GetProperty("frames").GetInt32());
    }

    [Fact]
    public void A_locked_session_refuses_input_text_from_the_default_seat()
    {
        var lockState = new SeatInjectorTests.SwitchLock();
        using var rig = new IpcTestRig((services, host) =>
        {
            services.Use<IKeymapLookup>(new IpcInputTests.TableLookup());
            services.Use<ILockState>(lockState);
            services.Use<ISyntheticInput>(new Basin.Seat.SeatSyntheticInput(host.Seat, services));
        });
        var binder = new Basin.Seat.Backends.SeatBinder(
            rig.Host.Seat, rig.Host.Layout, new LayoutPointer(rig.Host.Layout), new Basin.Desktop.CursorController(rig.Host.Layout));
        var keys = new List<uint>();
        binder.Key += (_, key, pressed) => keys.Add(key);
        rig.Server.Start();
        var peer = rig.Connect();

        using var typed = JsonDocument.Parse(peer.Call("""{"method":"input/text","params":{"text":"a","via":"keymap"}}"""));
        Assert.False(typed.RootElement.TryGetProperty("error", out var error), error.ToString());
        Assert.NotEmpty(keys);

        lockState.IsLocked = true;
        keys.Clear();
        using var refused = JsonDocument.Parse(peer.Call("""{"method":"input/text","params":{"text":"a","via":"keymap"}}"""));
        Assert.True(refused.RootElement.TryGetProperty("error", out _));
        Assert.Empty(keys);
    }

    [Fact]
    public void A_drag_steps_once_per_presented_frame_and_replies_once_the_window_settles()
    {
        var diagnostics = new IpcFullRig.QuietDiagnostics();
        var clock = new Basin.Capabilities.Defaults.FrameClock();
        var model = new TestToplevelModel();
        using var rig = new IpcTestRig((services, host) =>
        {
            services.Use<ISceneDiagnostics>(diagnostics);
            services.Use<IFrameClock>(clock);
            services.Use<IToplevelModel>(model);
        });
        var window = MappedToplevel.Map(rig.Host, rig.Host.Client, 60, 50, 0xFF0000FF);
        _ = model.Add("a terminal", "foot", window.ServerSurface, new Box(0, 0, 60, 50));
        var synthetic = new IpcInputTests.RecordingSynthetic();
        rig.Server.SyntheticInput = synthetic;
        rig.Server.Start();
        var peer = rig.Connect();
        peer.Send("""{"id":3,"method":"diag/drag","params":{"window":"foot","dx":30,"dy":0,"steps":3,"interval_ms":1000}}""");
        rig.Pump();
        Assert.Equal(["move 60 15"], synthetic.Log);

        var frames = 0;
        while (synthetic.Log.Count < 6 && frames < 20)
        {
            frames++;
            clock.BeginFrame(rig.Host.Output, frames);
            Assert.Equal(1 + frames, synthetic.Log.Count);
        }

        Assert.Equal(["move 60 15", "button 272 True", "move 70 15", "move 80 15", "move 90 15", "button 272 False"], synthetic.Log);
        clock.BeginFrame(rig.Host.Output, frames + 1);
        using var reply = JsonDocument.Parse(peer.Receive(400));
        var result = reply.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("settled").GetBoolean());
        Assert.Equal(5, result.GetProperty("frames").GetInt32());
    }

    [Fact]
    public void Damage_events_carry_a_stack_only_to_the_subscriber_that_asked()
    {
        var diagnostics = new IpcFullRig.QuietDiagnostics();
        using var rig = IpcFullRig.Create(services: services => services.Use<ISceneDiagnostics>(diagnostics));
        var plain = rig.Connect();
        var stacked = rig.Connect();
        _ = plain.Call("""{"method":"ipc/subscribe","params":{"events":["diag/damage"]}}""");
        Assert.False(Assert.Single(diagnostics.DamageWatchers).Stacks);
        using var bad = JsonDocument.Parse(stacked.Call(
            """{"method":"ipc/subscribe","params":{"events":["diag/damage"],"options":{"diag/damage":{"loud":true}}}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, bad.RootElement.GetProperty("error").GetProperty("code").GetString());
        _ = stacked.Call("""{"method":"ipc/subscribe","params":{"events":["diag/damage"],"options":{"diag/damage":{"stack":true}}}}""");
        Assert.True(Assert.Single(diagnostics.DamageWatchers).Stacks);

        diagnostics.DamageWatchers[0].Sink(new DamageInfo("tree/probe", new Box(1, 2, 3, 4), "at somewhere"));
        using var toPlain = JsonDocument.Parse(plain.Receive());
        using var toStacked = JsonDocument.Parse(stacked.Receive());
        Assert.False(toPlain.RootElement.GetProperty("data").TryGetProperty("stack", out _));
        Assert.Equal("at somewhere", toStacked.RootElement.GetProperty("data").GetProperty("stack").GetString());

        _ = stacked.Call("""{"method":"ipc/unsubscribe","params":{"events":["diag/damage"]}}""");
        Assert.False(Assert.Single(diagnostics.DamageWatchers).Stacks);
        _ = plain.Call("""{"method":"ipc/unsubscribe","params":{}}""");
        Assert.Empty(diagnostics.DamageWatchers);
    }

    [Fact]
    public void Input_events_name_the_window_and_carry_motion_only_to_who_asked()
    {
        var input = new IpcFullRig.QuietInputDiagnostics();
        using var rig = IpcFullRig.Create(services: services => services.Use<IInputDiagnostics>(input));
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        var window = MappedToplevel.Map(rig.Host, rig.Host.Client, 60, 50, 0xFF0000FF);
        var id = model.Add("a terminal", "foot", window.ServerSurface, new Box(0, 0, 60, 50));
        var plain = rig.Connect();
        var moving = rig.Connect();
        _ = plain.Call("""{"method":"diag/input-trace","params":{"mode":"on"}}""");
        _ = moving.Call("""{"method":"ipc/subscribe","params":{"events":["diag/input"],"options":{"diag/input":{"motion":true}}}}""");
        Assert.True(Assert.Single(input.Watchers).Motion);

        input.Watchers[0].Sink(new InputTraceEvent("motion", window.ServerSurface, 3, 4, 3, 4, 0, false, 0, null));
        input.Watchers[0].Sink(new InputTraceEvent("button", window.ServerSurface, 3, 4, 3, 4, 272, true, 0, null));
        using var first = JsonDocument.Parse(moving.Receive());
        Assert.Equal("motion", first.RootElement.GetProperty("data").GetProperty("kind").GetString());
        using var button = JsonDocument.Parse(plain.Receive());
        var data = button.RootElement.GetProperty("data");
        Assert.Equal(("button", id, "foot"), (data.GetProperty("kind").GetString(), data.GetProperty("window").GetUInt64(), data.GetProperty("app_id").GetString()));

        using var movingButton = JsonDocument.Parse(moving.Receive());
        Assert.Equal("button", movingButton.RootElement.GetProperty("data").GetProperty("kind").GetString());
        _ = moving.Call("""{"method":"ipc/unsubscribe","params":{}}""");
        Assert.False(Assert.Single(input.Watchers).Motion);
        _ = plain.Call("""{"method":"diag/input-trace","params":{"mode":"off"}}""");
        Assert.Empty(input.Watchers);
    }

    [Fact]
    public void A_commit_subscription_names_a_target_and_gets_only_its_commits()
    {
        using var rig = IpcFullRig.Create();
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        var foot = MappedToplevel.Map(rig.Host, rig.Host.Client, 60, 50, 0xFF0000FF);
        var other = MappedToplevel.Map(rig.Host, rig.Host.Client, 30, 20, 0xFF00FF00);
        var id = model.Add("a terminal", "foot", foot.ServerSurface, new Box(0, 0, 60, 50));
        _ = model.Add("other", "other", other.ServerSurface, new Box(0, 0, 30, 20));
        var peer = rig.Connect();
        using var bare = JsonDocument.Parse(peer.Call("""{"method":"ipc/subscribe","params":{"events":["diag/commit"]}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, bare.RootElement.GetProperty("error").GetProperty("code").GetString());
        _ = peer.Call("""{"method":"diag/commit-trace","params":{"on":true,"target":"foot"}}""");

        other.Surface.Attach(other.Buffer.Proxy, 0, 0);
        other.Surface.Commit();
        foot.Surface.Attach(foot.Buffer.Proxy, 0, 0);
        foot.Surface.DamageBuffer(1, 2, 3, 4);
        foot.Surface.Commit();
        rig.Host.PumpToServer();
        using var commit = JsonDocument.Parse(peer.Receive());
        var data = commit.RootElement.GetProperty("data");
        Assert.Equal(("commit", id, 60), (data.GetProperty("kind").GetString(), data.GetProperty("window").GetUInt64(), data.GetProperty("width").GetInt32()));
        Assert.Equal(3, data.GetProperty("damage")[0].GetProperty("width").GetInt32());
        _ = peer.Call("""{"method":"diag/commit-trace","params":{"on":false,"target":"foot"}}""");
    }

    [Fact]
    public void Selectors_reach_popups_layers_x11_windows_and_nodes()
    {
        var diagnostics = new IpcFullRig.QuietDiagnostics();
        var popups = new FakePopups();
        var layers = new FakeLayers();
        using var rig = IpcFullRig.Create(services: services =>
        {
            services.Use<ISceneDiagnostics>(diagnostics);
            services.Use<IPopupDirectory>(popups);
            services.Use<ILayerDirectory>(layers);
        });
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        var foot = MappedToplevel.Map(rig.Host, rig.Host.Client, 60, 50, 0xFF0000FF);
        var menu = MappedToplevel.Map(rig.Host, rig.Host.Client, 20, 10, 0xFF00FF00);
        var xterm = MappedToplevel.Map(rig.Host, rig.Host.Client, 30, 20, 0xFF0000FF);
        var footId = model.Add("a terminal", "foot", foot.ServerSurface, new Box(0, 0, 60, 50));
        var xtermId = model.Add("xterm", "XTerm", xterm.ServerSurface, new Box(0, 0, 30, 20));
        model.SetX11(xtermId, ":1", X11Density.Native);
        popups.Owner = foot.ServerSurface;
        popups.Popups.Add(menu.ServerSurface);
        layers.Layers.Add(new LayerInfo("panel", menu.ServerSurface, "HEADLESS-1"));
        var peer = rig.Connect();

        string Record(string window)
        {
            using var reply = JsonDocument.Parse(peer.Call(
                $$$"""{"method":"diag/record-window","params":{"window":"{{{window}}}","frames":1,"path":"/tmp/never.png","margin":0}}"""));
            return reply.RootElement.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString()! : "ok";
        }

        Assert.Equal("ok", Record("foot/popup"));
        Assert.Equal(new Box(5, 6, 30, 20), diagnostics.LastRecording!.Follow!());
        Assert.Equal("ok", Record("foot/popup:0"));
        Assert.Equal(IpcErrorCodes.NotFound, Record("foot/popup:1"));
        Assert.Equal(IpcErrorCodes.NotFound, Record("XTerm/popup"));
        Assert.Equal("ok", Record("layer:panel"));
        Assert.Equal(IpcErrorCodes.NotFound, Record("layer:dock"));
        Assert.Equal("ok", Record("node:tree/probe"));
        Assert.Equal(new Box(4, 5, 10, 6), diagnostics.LastRecording!.Follow!());
        Assert.Equal(IpcErrorCodes.NotFound, Record("node:tree/none"));
        Assert.Equal(IpcErrorCodes.NotFound, Record("x11:foot"));

        using var parts = JsonDocument.Parse(peer.Call("""{"method":"diag/parts","params":{"window":"x11:XTerm"}}"""));
        Assert.Equal(xtermId, parts.RootElement.GetProperty("result").GetProperty("window").GetUInt64());
        using var layerParts = JsonDocument.Parse(peer.Call("""{"method":"diag/parts","params":{"window":"layer:panel"}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, layerParts.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var targets = JsonDocument.Parse(peer.Call("""{"method":"diag/targets","params":{}}"""));
        var listed = targets.RootElement.GetProperty("result").GetProperty("targets").EnumerateArray()
            .Select(t => $"{t.GetProperty("kind").GetString()} {t.GetProperty("selector").GetString()}").ToArray();
        Assert.Contains($"window {footId}", listed);
        Assert.Contains($"popup {footId}/popup:0", listed);
        Assert.Contains("x11 x11:XTerm", listed);
        Assert.Contains("layer layer:panel", listed);
    }

    private sealed class FakePopups : IPopupDirectory
    {
        public Basin.Surface? Owner { get; set; }

        public List<Basin.Surface> Popups { get; } = [];

        public void CollectPopups(Basin.Surface owner, List<Basin.Surface> into)
        {
            if (owner == Owner)
            {
                into.AddRange(Popups);
            }
        }
    }

    private sealed class FakeLayers : ILayerDirectory
    {
        public List<LayerInfo> Layers { get; } = [];

        public void CollectLayers(List<LayerInfo> into) => into.AddRange(Layers);
    }

    [Fact]
    public void Without_the_capability_the_diag_group_is_absent()
    {
        using var rig = new IpcTestRig();
        Assert.DoesNotContain(rig.Server.Methods.Names, name => name.StartsWith("diag/", StringComparison.Ordinal));
    }
}
