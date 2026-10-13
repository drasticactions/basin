using Basin.Config;
using Basin.Diagnostics;
using Basin.Greetd;
using Tomlyn;
using Xunit;

namespace Basin.Tests;

public sealed class GreeterPiecesTests
{
    private sealed class CollectingSink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            Lines.Add($"{level}:{message}");
    }

    private static (BasinLogger Log, CollectingSink Sink) Logger()
    {
        var sink = new CollectingSink();
        BasinLog.Sink = sink;
        BasinLog.Level = BasinLogLevel.Trace;
        return (BasinLog.For("test"), sink);
    }

    private static void Restore() => BasinLog.Sink = null;

    private static void Settle(CompositorTestHost host, GreetdLogin login) =>
        PortalBusTests.PumpUntil(host, () => login.State != GreetdLoginState.Waiting && login.State != GreetdLoginState.Starting);

    [Fact]
    public void Settings_default_to_basin_greeters_defaults()
    {
        var (log, _) = Logger();
        try
        {
            var settings = GreeterSettings.Read(new TomlReader(Toml.ToModel(""), log));

            Assert.True(settings.ListUsers);
            Assert.Empty(settings.HideUsers);
            Assert.Equal(0u, settings.MinUid);
            Assert.Equal("", settings.DefaultSession);
            Assert.Equal(["startx"], settings.X11Wrapper);
            Assert.Equal("/etc/greetd/environments", settings.Environments);
            Assert.False(settings.AllowCommand);
            Assert.Empty(settings.Layouts);
            Assert.Equal("auto", settings.OnScreen);
            Assert.Equal("m", settings.OnScreenSize);
            Assert.True(settings.Power);
            Assert.True(settings.Battery && settings.Network && settings.NetworkJoin);
            Assert.False(settings.HighContrast);
            Assert.Equal(1.0, settings.TextScale);
            Assert.Equal("toggle", settings.ScreenReader);
            Assert.Equal(["orca", "--replace"], settings.Orca);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void Settings_consume_only_their_own_keys_and_share_a_table_with_the_program()
    {
        var (log, sink) = Logger();
        try
        {
            var reader = new TomlReader(Toml.ToModel("""
                [users]
                hide = ["root", "nobody"]
                min_uid = 1000

                [keyboard]
                layouts = ["us", "us(intl)"]
                on_screen = "always"
                look = "luna"
                mystery = 1

                [accessibility]
                text_scale = 4.0
                orca = "orca"

                [look]
                clock = "%H"
                """), log);

            var settings = GreeterSettings.Read(reader);
            Assert.Equal("luna", reader.Section("keyboard")!.Text("look"));
            Assert.Equal("%H", reader.Section("look")!.Text("clock"));
            reader.ReportUnknown();

            Assert.Equal(new HashSet<string> { "root", "nobody" }, settings.HideUsers);
            Assert.Equal(1000u, settings.MinUid);
            Assert.Equal(["us", "us(intl)"], settings.Layouts);
            Assert.Equal("always", settings.OnScreen);
            Assert.Equal(2.0, settings.TextScale);
            Assert.Equal(["orca"], settings.Orca);
            Assert.Contains(sink.Lines, line => line.Contains("unknown key 'keyboard.mystery'", StringComparison.Ordinal));
            Assert.DoesNotContain(sink.Lines, line => line.Contains("'keyboard.look'", StringComparison.Ordinal));
            Assert.DoesNotContain(sink.Lines, line => line.Contains("'keyboard.layouts'", StringComparison.Ordinal));
            Assert.DoesNotContain(sink.Lines, line => line.Contains("'look.clock'", StringComparison.Ordinal));
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void Memory_round_trips_and_keeps_what_it_did_not_change()
    {
        var directory = Directory.CreateTempSubdirectory("basin-memory-");
        try
        {
            var path = Path.Combine(directory.FullName, "state.toml");
            var memory = GreeterMemory.Load(path, BasinLog.For("test"));
            Assert.Null(memory.LastUser);
            Assert.True(memory.Remember("ada", "sway", "us(intl)"));
            Assert.True(memory.Remember("grace", "i3", null));
            Assert.True(memory.Remember("ada", null, "de"));

            var again = GreeterMemory.Load(path, BasinLog.For("test"));
            Assert.Equal("ada", again.LastUser);
            Assert.Equal("sway", again.SessionOf("ada"));
            Assert.Equal("de", again.LayoutOf("ada"));
            Assert.Equal("i3", again.SessionOf("grace"));
            Assert.Null(again.LayoutOf("grace"));
            Assert.Null(again.SessionOf("nobody"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Memory_reads_the_file_an_installed_basin_greeter_wrote()
    {
        var directory = Directory.CreateTempSubdirectory("basin-memory-");
        try
        {
            var path = Path.Combine(directory.FullName, "state.toml");
            File.WriteAllText(path, """
                last_user = "basintest"

                [users.basintest]
                session = "tinycomp-test"
                layout = "us"

                [users."with space"]
                session = "sway"
                """);

            var memory = GreeterMemory.Load(path, BasinLog.For("test"));

            Assert.Equal("basintest", memory.LastUser);
            Assert.Equal("tinycomp-test", memory.SessionOf("basintest"));
            Assert.Equal("us", memory.LayoutOf("basintest"));
            Assert.Equal("sway", memory.SessionOf("with space"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_default_memory_path_follows_the_state_directory()
    {
        var saved = Environment.GetEnvironmentVariable("STATE_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("STATE_DIRECTORY", null);
            Assert.Equal("/var/lib/luna-wm-greeter/state.toml", GreeterMemory.DefaultPath("luna-wm-greeter"));
            Environment.SetEnvironmentVariable("STATE_DIRECTORY", "/run/state/one:/run/state/two");
            Assert.Equal("/run/state/one/state.toml", GreeterMemory.DefaultPath("basin-greeter"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STATE_DIRECTORY", saved);
        }
    }

    [Fact]
    public void The_loopback_server_holds_a_whole_conversation()
    {
        using var host = new CompositorTestHost();
        var (server, socket) = LoopbackGreetd.Create(host.Loop, new LoopbackGreetdOptions { Info = "Hello", Prompt = "Secret:", Password = "pw" });
        using (server)
        {
            GreetdStartSession? started = null;
            server.SessionStarted += start => started = start;
            using var client = GreetdClient.Adopt(host.Loop, socket);
            using var login = new GreetdLogin(client);

            login.Start("anyone");
            Settle(host, login);
            Assert.Equal((GreetdAuthMessageType.Info, "Hello"), (login.PromptType, login.PromptText));
            login.Acknowledge();
            Settle(host, login);
            Assert.Equal((GreetdAuthMessageType.Secret, "Secret:"), (login.PromptType, login.PromptText));
            login.Answer("wrong"u8);
            Settle(host, login);
            Assert.Equal(GreetdErrorType.AuthError, login.FailureKind);

            login.Start("anyone");
            Settle(host, login);
            login.Acknowledge();
            Settle(host, login);
            login.Answer("pw"u8);
            Settle(host, login);
            Assert.Equal(GreetdLoginState.Ready, login.State);
            login.StartSession(new GreetdSessionCommand(["sway"], ["XKB_DEFAULT_LAYOUT=us"]));
            Settle(host, login);

            Assert.Equal(GreetdLoginState.Started, login.State);
            Assert.NotNull(started);
            Assert.Equal(["sway"], started.Cmd);
            Assert.Equal(["XKB_DEFAULT_LAYOUT=us"], started.Env);
        }
    }

    [Fact]
    public void A_loopback_server_without_info_goes_straight_to_the_secret()
    {
        using var host = new CompositorTestHost();
        var (server, socket) = LoopbackGreetd.Create(host.Loop, new LoopbackGreetdOptions { Info = null });
        using (server)
        {
            using var client = GreetdClient.Adopt(host.Loop, socket);
            using var login = new GreetdLogin(client);

            login.Start("anyone");
            Settle(host, login);

            Assert.Equal((GreetdAuthMessageType.Secret, "Password:"), (login.PromptType, login.PromptText));
            login.Answer("demo"u8);
            Settle(host, login);
            Assert.Equal(GreetdLoginState.Ready, login.State);
        }
    }
}
