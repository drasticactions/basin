using Basin.Capabilities;
using Basin.Desktop;
using Basin.Desktop.Protocol;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class SessionManagementTests
{
    [Fact]
    public void A_new_session_is_minted_and_a_known_one_is_restored()
    {
        using var host = new CompositorTestHost();
        var store = new MemorySessionStore();
        using var manager = new SessionManager(host.Display, store);

        var proxy = BindSessions(host);
        string? created = null;
        var restored = 0;
        var fresh = proxy.GetSession(XdgSessionManagerV1.Reason.Launch, null);
        fresh.Created += (_, e) => created = e.SessionId;
        host.PumpUntil(() => created is not null);
        Assert.Equal(store.LastMinted, created);

        fresh.Destroy();
        host.PumpToServer();

        var again = proxy.GetSession(XdgSessionManagerV1.Reason.SessionRestore, created);
        again.Restored += (_, _) => restored++;
        host.PumpUntil(() => restored == 1);
    }

    [Fact]
    public void The_same_client_reclaiming_a_live_session_is_in_use()
    {
        using var host = new CompositorTestHost();
        var store = new MemorySessionStore();
        using var manager = new SessionManager(host.Display, store);

        var proxy = BindSessions(host);
        string? created = null;
        var session = proxy.GetSession(XdgSessionManagerV1.Reason.Launch, null);
        session.Created += (_, e) => created = e.SessionId;
        host.PumpUntil(() => created is not null);

        proxy.GetSession(XdgSessionManagerV1.Reason.Launch, created);
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("xdg_session_manager_v1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_client_taking_a_live_session_replaces_it_rather_than_failing()
    {
        using var host = new CompositorTestHost();
        var store = new MemorySessionStore();
        using var manager = new SessionManager(host.Display, store);

        var first = BindSessions(host);
        string? created = null;
        var replaced = 0;
        var original = first.GetSession(XdgSessionManagerV1.Reason.Launch, null);
        original.Created += (_, e) => created = e.SessionId;
        original.Replaced += (_, _) => replaced++;
        host.PumpUntil(() => created is not null);

        var other = host.ConnectClient();
        var second = BindSessions(host, other);
        var restored = 0;
        var taken = second.GetSession(XdgSessionManagerV1.Reason.Recover, created);
        taken.Restored += (_, _) => restored++;
        host.PumpUntil(() => restored == 1 && replaced == 1);
    }

    [Fact]
    public void An_invalid_reason_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new SessionManager(host.Display, new MemorySessionStore());

        var proxy = BindSessions(host);
        proxy.GetSession((XdgSessionManagerV1.Reason)99, null);
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("xdg_session_manager_v1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_applies_size_and_states_in_the_first_configure_and_announces_once()
    {
        using var host = new CompositorTestHost();
        var store = new MemorySessionStore();
        store.Remember("s1", "main", new ToplevelSessionState
        {
            Geometry = new Box(400, 300, 220, 180),
            OutputLayoutId = "layout-1",
            States = ToplevelSessionStates.Maximized,
        });

        using var manager = new SessionManager(host.Display, store);
        var client = host.Client;
        var proxy = BindSessions(host);

        Basin.Shell.Xdg.XdgToplevelWindow? server = null;
        Basin.Shell.Xdg.ToplevelRestore? seen = null;
        var restoringAtFirstConfigure = false;
        var configures = 0;
        host.Shell.NewToplevel += toplevel =>
        {
            server ??= toplevel;
            toplevel.Restored += r => seen ??= r;
            toplevel.Configuring += () =>
            {
                if (configures++ == 0)
                {
                    restoringAtFirstConfigure = toplevel.Restoring is not null;
                }
            };
        };

        var session = proxy.GetSession(XdgSessionManagerV1.Reason.SessionRestore, "s1");
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();

        var width = 0;
        var height = 0;
        var states = new List<uint>();
        var restoredEvents = 0;
        toplevelProxy.Configure += (_, e) =>
        {
            width = e.Width;
            height = e.Height;
            states.Clear();
            for (var offset = 0; offset + 4 <= e.States.Length; offset += 4)
            {
                states.Add(BitConverter.ToUInt32(e.States, offset));
            }
        };
        xdgSurface.Configure += (_, e) => xdgSurface.AckConfigure(e.Serial);

        var handle = session.RestoreToplevel(toplevelProxy, "main");
        handle.Restored += (_, _) => restoredEvents++;

        host.PumpToServer();
        Assert.NotNull(server);
        Assert.NotNull(seen);
        Assert.Equal(new Box(400, 300, 220, 180), seen!.Value.State.Geometry);
        Assert.Equal("layout-1", seen.Value.State.OutputLayoutId);

        surface.Commit();
        host.PumpUntil(() => width == 220);
        Assert.Equal(180, height);
        Assert.Contains(1u, states);
        Assert.Equal(1, restoredEvents);

        Assert.True(restoringAtFirstConfigure);
        Assert.Null(server!.Restoring);
    }

    [Fact]
    public void Add_toplevel_never_announces_a_restore()
    {
        using var host = new CompositorTestHost();
        var store = new MemorySessionStore();
        store.Remember("s1", "main", new ToplevelSessionState { Geometry = new Box(0, 0, 220, 180) });
        using var manager = new SessionManager(host.Display, store);

        var client = host.Client;
        var proxy = BindSessions(host);
        var session = proxy.GetSession(XdgSessionManagerV1.Reason.Launch, "s1");

        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();
        xdgSurface.Configure += (_, e) => xdgSurface.AckConfigure(e.Serial);

        var restoredEvents = 0;
        var handle = session.AddToplevel(toplevelProxy, "main");
        handle.Restored += (_, _) => restoredEvents++;
        surface.Commit();
        host.PumpToClient();
        host.PumpToClient();

        Assert.Equal(0, restoredEvents);
    }

    [Fact]
    public void Restoring_after_the_first_commit_is_already_mapped()
    {
        using var host = new CompositorTestHost();
        using var manager = new SessionManager(host.Display, new MemorySessionStore());

        var client = host.Client;
        var proxy = BindSessions(host);
        var session = proxy.GetSession(XdgSessionManagerV1.Reason.Launch, null);

        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevelProxy = xdgSurface.GetToplevel();
        xdgSurface.Configure += (_, e) => xdgSurface.AckConfigure(e.Serial);
        surface.Commit();
        host.PumpToClient();

        session.RestoreToplevel(toplevelProxy, "late");
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("xdg_session_v1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_taken_twice_in_one_session_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new SessionManager(host.Display, new MemorySessionStore());

        var client = host.Client;
        var proxy = BindSessions(host);
        var session = proxy.GetSession(XdgSessionManagerV1.Reason.Launch, null);

        var first = MakeToplevel(host, client);
        var second = MakeToplevel(host, client);
        session.AddToplevel(first, "main");
        session.AddToplevel(second, "main");
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("xdg_session_v1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_toplevel_added_twice_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new SessionManager(host.Display, new MemorySessionStore());

        var client = host.Client;
        var proxy = BindSessions(host);
        var session = proxy.GetSession(XdgSessionManagerV1.Reason.Launch, null);

        var toplevel = MakeToplevel(host, client);
        session.AddToplevel(toplevel, "one");
        session.AddToplevel(toplevel, "two");
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("xdg_session_v1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_drops_the_session_from_the_store()
    {
        using var host = new CompositorTestHost();
        var store = new MemorySessionStore();
        using var manager = new SessionManager(host.Display, store);

        var proxy = BindSessions(host);
        string? created = null;
        var session = proxy.GetSession(XdgSessionManagerV1.Reason.Launch, null);
        session.Created += (_, e) => created = e.SessionId;
        host.PumpUntil(() => created is not null);

        store.Remember(created!, "main", new ToplevelSessionState());
        session.Remove();
        host.PumpToServer();

        Assert.Contains(created!, store.Forgotten);
        Assert.False(store.IsValidSessionId(created!));
    }

    private static Basin.Shell.Xdg.Protocol.XdgToplevel MakeToplevel(CompositorTestHost host, ShmTestClient client)
    {
        var surface = client.Compositor.CreateSurface();
        var xdgSurface = client.WmBase!.GetXdgSurface(surface);
        var toplevel = xdgSurface.GetToplevel();
        xdgSurface.Configure += (_, e) => xdgSurface.AckConfigure(e.Serial);
        host.PumpToServer();
        return toplevel;
    }

    private static XdgSessionManagerV1 BindSessions(CompositorTestHost host, ShmTestClient? only = null)
    {
        var client = only ?? host.Client;
        XdgSessionManagerV1? proxy = null;
        var registry = client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "xdg_session_manager_v1")
            {
                proxy = registry.Bind<XdgSessionManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}

internal sealed class MemorySessionStore : ISessionStore
{
    private readonly Dictionary<string, Dictionary<string, ToplevelSessionState>> _sessions = [];
    private int _counter;

    public string? LastMinted { get; private set; }

    public List<string> Forgotten { get; } = [];

    public void Remember(string sessionId, string name, in ToplevelSessionState state)
    {
        if (!_sessions.TryGetValue(sessionId, out var toplevels))
        {
            _sessions[sessionId] = toplevels = [];
        }

        toplevels[name] = state;
    }

    public string? CreateSessionId()
    {
        LastMinted = $"session-{++_counter}";
        _sessions[LastMinted] = [];
        return LastMinted;
    }

    public bool IsValidSessionId(string sessionId) => _sessions.ContainsKey(sessionId);

    public bool TryRestore(string sessionId, string toplevelName, SessionRestoreReason reason, out ToplevelSessionState state)
    {
        state = default;
        return _sessions.TryGetValue(sessionId, out var toplevels) && toplevels.TryGetValue(toplevelName, out state);
    }

    public void Save(string sessionId, string toplevelName, in ToplevelSessionState state) =>
        Remember(sessionId, toplevelName, state);

    public void ForgetToplevel(string sessionId, string toplevelName)
    {
        if (_sessions.TryGetValue(sessionId, out var toplevels))
        {
            toplevels.Remove(toplevelName);
        }
    }

    public void Forget(string sessionId)
    {
        _sessions.Remove(sessionId);
        Forgotten.Add(sessionId);
    }
}

public sealed class FileSessionStoreTests
{
    private static (FileSessionStore Store, string Directory) Fresh()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"basin-store-{Guid.NewGuid():n}");
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", directory);
        try
        {
            return (new FileSessionStore("basin-tests"), directory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_STATE_HOME", null);
        }
    }

    [Fact]
    public void The_workspace_name_round_trips_through_the_file()
    {
        var (store, directory) = Fresh();
        try
        {
            var session = store.CreateSessionId()!;
            store.Save(session, "main", new ToplevelSessionState
            {
                Geometry = new Box(12, 34, 320, 240),
                States = ToplevelSessionStates.Maximized,
                OutputLayoutId = "layout-1",
                WorkspaceName = "scratch",
            });

            Environment.SetEnvironmentVariable("XDG_STATE_HOME", directory);
            FileSessionStore reloaded;
            try
            {
                reloaded = new FileSessionStore("basin-tests");
            }
            finally
            {
                Environment.SetEnvironmentVariable("XDG_STATE_HOME", null);
            }

            Assert.True(reloaded.TryRestore(session, "main", SessionRestoreReason.SessionRestore, out var state));
            Assert.Equal("scratch", state.WorkspaceName);
            Assert.Equal(new Box(12, 34, 320, 240), state.Geometry);
            Assert.Equal("layout-1", state.OutputLayoutId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_pre_workspace_file_restores_with_a_null_workspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"basin-store-{Guid.NewGuid():n}");
        var app = Path.Combine(directory, "basin-tests");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "sessions.tsv"), "old-session\tmain\t5\t6\t100\t80\t1\tlayout-1\n");
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", directory);
        try
        {
            var store = new FileSessionStore("basin-tests");
            Assert.True(store.TryRestore("old-session", "main", SessionRestoreReason.SessionRestore, out var state));
            Assert.Null(state.WorkspaceName);
            Assert.Equal(new Box(5, 6, 100, 80), state.Geometry);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_STATE_HOME", null);
            Directory.Delete(directory, recursive: true);
        }
    }
}
