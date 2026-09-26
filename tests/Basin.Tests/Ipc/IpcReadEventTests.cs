using System.Text.Json;
using Basin.Capabilities;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcReadEventTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static JsonElement Result(IpcTestPeer peer, string request) => Parse(peer.Call(request)).GetProperty("result");

    [Fact]
    public void Absent_capabilities_leave_their_groups_out()
    {
        using var rig = new IpcTestRig();
        var peer = rig.Connect();
        var methods = Result(peer, """{"method":"ipc/methods"}""").GetProperty("methods").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.DoesNotContain(IpcMethodNames.WindowsList, methods);
        Assert.DoesNotContain(IpcMethodNames.WorkspacesList, methods);
        var reply = Parse(peer.Call("""{"method":"windows/list"}"""));
        Assert.Equal(IpcErrorCodes.UnknownMethod, reply.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(Result(peer, """{"method":"ipc/events"}""").GetProperty("events").EnumerateArray());
        Assert.True(Parse(peer.Call("""{"method":"ipc/version"}""")).TryGetProperty("result", out _));
    }

    [Fact]
    public void Windows_list_get_and_stack_read_the_model()
    {
        var model = new TestToplevelModel();
        var stack = new TestToplevelStack();
        using var rig = new IpcTestRig(services =>
        {
            services.Use<IToplevelModel>(model);
            services.Use<IToplevelStack>(stack);
        });
        var first = model.Add("one", "app.one", geometry: new Box(10, 20, 30, 40));
        var second = model.Add("two", "app.two");
        model.SetState(second, ToplevelState.Activated | ToplevelState.Maximized);
        stack.SetOrder(second, first);
        var peer = rig.Connect();

        var windows = Result(peer, """{"method":"windows/list"}""").GetProperty("windows");
        Assert.Equal(2, windows.GetArrayLength());
        Assert.Equal("app.one", windows[0].GetProperty("app_id").GetString());
        Assert.Equal(30, windows[0].GetProperty("geometry").GetProperty("width").GetInt32());
        Assert.True(windows[1].GetProperty("state").GetProperty("maximized").GetBoolean());

        var one = Result(peer, $$$"""{"method":"windows/get","params":{"id":{{{first}}}}}""");
        Assert.Equal("one", one.GetProperty("title").GetString());
        var missing = Parse(peer.Call("""{"method":"windows/get","params":{"id":99}}"""));
        Assert.Equal(IpcErrorCodes.NotFound, missing.GetProperty("error").GetProperty("code").GetString());

        var ids = Result(peer, """{"method":"windows/stack"}""").GetProperty("ids");
        Assert.Equal([second, first], ids.EnumerateArray().Select(e => e.GetUInt64()).ToArray());
    }

    [Fact]
    public void Outputs_list_names_the_layout()
    {
        using var rig = new IpcTestRig(services => services.Use(new OutputLayout()));
        var layout = rig.Services.Require<OutputLayout>();
        layout.Add(rig.Host.Output, 100, 0);
        var peer = rig.Connect();
        var result = Result(peer, """{"method":"outputs/list"}""");
        var output = result.GetProperty("outputs")[0];
        Assert.Equal(rig.Host.Output.Name, output.GetProperty("name").GetString());
        Assert.Equal(100, output.GetProperty("geometry").GetProperty("x").GetInt32());
        Assert.Equal(160, output.GetProperty("mode").GetProperty("width").GetInt32());
        Assert.Equal("normal", output.GetProperty("transform").GetString());
    }

    [Fact]
    public void Idle_inhibit_belongs_to_the_connection()
    {
        var idle = new TestIdle();
        using var rig = new IpcTestRig(services => services.Use<IIdleSource>(idle));
        var peer = rig.Connect();
        var token = Result(peer, """{"method":"idle/inhibit"}""").GetProperty("token").GetString();
        Assert.Equal(1, idle.Inhibitors);
        Assert.True(Result(peer, """{"method":"idle/status"}""").GetProperty("inhibited").GetBoolean());
        var wrong = Parse(peer.Call("""{"method":"idle/uninhibit","params":{"token":"inhibit-77"}}"""));
        Assert.Equal(IpcErrorCodes.NotFound, wrong.GetProperty("error").GetProperty("code").GetString());
        _ = Result(peer, $$$"""{"method":"idle/uninhibit","params":{"token":"{{{token}}}"}}""");
        Assert.Equal(0, idle.Inhibitors);

        _ = Result(peer, """{"method":"idle/inhibit"}""");
        Assert.Equal(1, idle.Inhibitors);
        peer.Dispose();
        for (var i = 0; i < 20 && rig.Server.ConnectionCount > 0; i++)
        {
            rig.Pump();
        }

        Assert.Equal(0, idle.Inhibitors);
    }

    [Fact]
    public void No_subscriber_attaches_no_observer_and_unsubscribing_detaches()
    {
        var model = new TestToplevelModel();
        using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        var peer = rig.Connect();
        _ = Result(peer, """{"method":"windows/list"}""");
        Assert.Equal(0, model.ObserverCount);
        Assert.Equal(0, rig.Server.Events.AttachedSources);

        _ = Result(peer, """{"method":"ipc/subscribe","params":{"events":["window/changed","window/added"]}}""");
        Assert.Equal(1, model.ObserverCount);
        Assert.Equal(1, rig.Server.Events.AttachedSources);
        _ = Result(peer, """{"method":"ipc/subscribe","params":{"events":["window/changed"]}}""");
        Assert.Equal(1, model.ObserverCount);

        _ = Result(peer, """{"method":"ipc/unsubscribe","params":{"events":["window/changed"]}}""");
        Assert.Equal(1, model.ObserverCount);
        _ = Result(peer, """{"method":"ipc/unsubscribe"}""");
        Assert.Equal(0, model.ObserverCount);
        Assert.Equal(0, rig.Server.Events.AttachedSources);

        var unknown = Parse(peer.Call("""{"method":"ipc/subscribe","params":{"events":["window/nope"]}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, unknown.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Closing_a_subscriber_detaches_the_observer()
    {
        var model = new TestToplevelModel();
        using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        var peer = rig.Connect();
        _ = Result(peer, """{"method":"ipc/subscribe","params":{"events":["window/changed"]}}""");
        Assert.Equal(1, model.ObserverCount);
        peer.Dispose();
        for (var i = 0; i < 20 && rig.Server.ConnectionCount > 0; i++)
        {
            rig.Pump();
        }

        Assert.Equal(0, model.ObserverCount);
    }

    [Fact]
    public void Sixty_title_changes_in_one_iteration_give_one_event()
    {
        var model = new TestToplevelModel();
        using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model));
        var id = model.Add("start", "app");
        var peer = rig.Connect();
        _ = Result(peer, """{"method":"ipc/subscribe","params":{"events":["window/changed","window/focused","window/removed","window/added"]}}""");
        for (var i = 0; i < 60; i++)
        {
            model.Retitle(id, $"title {i}");
        }

        rig.Pump();
        var changed = Parse(peer.Receive());
        Assert.Equal("window/changed", changed.GetProperty("event").GetString());
        Assert.Equal("title 59", changed.GetProperty("data").GetProperty("title").GetString());
        rig.Pump();
        Assert.False(peer.HasFrame());

        model.SetState(id, ToplevelState.Activated);
        rig.Pump();
        Assert.Equal("window/changed", Parse(peer.Receive()).GetProperty("event").GetString());
        var focused = Parse(peer.Receive());
        Assert.Equal("window/focused", focused.GetProperty("event").GetString());
        Assert.Equal(id, focused.GetProperty("data").GetProperty("id").GetUInt64());

        var added = model.Add("new", "app2");
        model.Remove(id);
        rig.Pump();
        Assert.Equal("window/added", Parse(peer.Receive()).GetProperty("event").GetString());
        var removed = Parse(peer.Receive());
        Assert.Equal("window/removed", removed.GetProperty("event").GetString());
        Assert.Equal(id, removed.GetProperty("data").GetProperty("id").GetUInt64());
        Assert.NotEqual(0UL, added);
    }

    [Fact]
    public void Consumer_events_reach_subscribers_only()
    {
        using var rig = new IpcTestRig();
        rig.Server.Events.Declare("test/ping");
        var peer = rig.Connect();
        Assert.False(rig.Server.Events.HasSubscribers("test/ping"));
        _ = Result(peer, """{"method":"ipc/subscribe","params":{"events":["test/ping"]}}""");
        Assert.True(rig.Server.Events.HasSubscribers("test/ping"));
        var writer = rig.Server.Events.BeginEvent("test/ping");
        writer.WriteStartObject();
        writer.WriteNumber("n", 3);
        writer.WriteEndObject();
        rig.Server.Events.EndEvent();
        var message = Parse(peer.Receive());
        Assert.Equal(3, message.GetProperty("data").GetProperty("n").GetInt32());
    }

    internal sealed class TestIdle : IIdleSource
    {
        public int Inhibitors { get; private set; }

        public long IdleMillis => 5;

        public bool IsInhibited => Inhibitors > 0;

        public event Action? Activity;

        public event Action? InhibitionChanged;

        public void NotifyActivity() => Activity?.Invoke();

        public IDisposable Inhibit()
        {
            Inhibitors++;
            InhibitionChanged?.Invoke();
            return new Release(this);
        }

        private sealed class Release(TestIdle owner) : IDisposable
        {
            private bool _done;

            public void Dispose()
            {
                if (_done)
                {
                    return;
                }

                _done = true;
                owner.Inhibitors--;
                owner.InhibitionChanged?.Invoke();
            }
        }
    }
}
