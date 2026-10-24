using System.Text;
using Basin.Config;
using Basin.Diagnostics;
using Tomlyn;
using Tomlyn.Model;
using Xunit;

namespace Basin.Tests;

public sealed class WindowRuleTests
{
    private sealed class CollectingSink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            Lines.Add($"{level}:{message}");
    }

    [Fact]
    public void A_rule_with_no_criteria_matches_every_window()
    {
        var rule = new WindowRule();

        Assert.True(rule.Matches(new WindowFacts { AppId = "firefox", Title = "Mozilla Firefox" }));
        Assert.True(rule.Matches(new WindowFacts { AppId = "firefox" }));
        Assert.True(rule.Matches(default));
        Assert.True(rule.MatchesText("org.gnome.Nautilus", "Home"));
    }

    [Fact]
    public void A_steam_game_rule_matches_only_that_game()
    {
        var rule = new WindowRule { SteamGames = [570] };

        Assert.True(rule.Matches(new WindowFacts { SteamGame = 570 }));
        Assert.False(rule.Matches(new WindowFacts { SteamGame = 440 }));
        Assert.False(rule.Matches(new WindowFacts { AppId = "steam_app_570" }));
    }

    [Fact]
    public void A_process_rule_matches_the_comm_or_the_executable()
    {
        var rule = new WindowRule { Processes = ["dota2"] };

        Assert.True(rule.Matches(new WindowFacts { ProcessName = "dota2" }));
        Assert.True(rule.Matches(new WindowFacts { ProcessName = "main", Executable = "dota2" }));
        Assert.False(rule.Matches(new WindowFacts { ProcessName = "dota", Executable = "dota" }));
    }

    [Fact]
    public void Class_and_instance_match_separately()
    {
        var byClass = new WindowRule { X11Classes = ["Steam"] };
        var byInstance = new WindowRule { X11Instances = ["steamwebhelper"] };
        var facts = WindowFacts.ForX11("Steam", "steamwebhelper", "Steam", null, "normal", default);

        Assert.True(byClass.Matches(facts));
        Assert.True(byInstance.Matches(facts));
        Assert.False(byClass.Matches(facts with { X11Class = "steam" }));
        Assert.False(byInstance.Matches(WindowFacts.ForWayland("Steam", "Steam", false, default)));
    }

    [Fact]
    public void A_window_type_list_matches_any_member()
    {
        var rule = new WindowRule { WindowTypes = ["dialog", "utility"] };

        Assert.True(rule.Matches(new WindowFacts { WindowType = "dialog" }));
        Assert.True(rule.Matches(new WindowFacts { WindowType = "utility" }));
        Assert.False(rule.Matches(new WindowFacts { WindowType = "normal" }));
        Assert.True(rule.Matches(WindowFacts.ForWayland("app", "t", dialog: true, default)));
    }

    [Fact]
    public void Fact_keys_combine_with_app_and_title()
    {
        var rule = new WindowRule { AppIds = ["Steam"], WindowTypes = ["dialog"] };

        Assert.True(rule.Matches(new WindowFacts { AppId = "Steam", WindowType = "dialog" }));
        Assert.False(rule.Matches(new WindowFacts { AppId = "Steam", WindowType = "normal" }));
        Assert.False(rule.Matches(new WindowFacts { AppId = "firefox", WindowType = "dialog" }));
        Assert.False(rule.MatchesText("Steam", null));
    }

    [Fact]
    public void Each_fact_key_outranks_app_and_title()
    {
        var both = new WindowRule { AppIds = ["a"], TitleRegex = new System.Text.RegularExpressions.Regex("b") };
        var steam = new WindowRule { SteamGames = [570] };
        var steamAndProcess = new WindowRule { SteamGames = [570], Processes = ["dota2"] };

        Assert.Equal(3, both.Specificity);
        Assert.Equal(4, steam.Specificity);
        Assert.Equal(8, steamAndProcess.Specificity);
        var ordered = WindowRule.MostSpecificFirst([both, steam, steamAndProcess]);
        Assert.Same(steamAndProcess, ordered[0]);
        Assert.Same(steam, ordered[1]);
        Assert.Same(both, ordered[2]);
    }

    [Fact]
    public void The_toml_keys_parse_and_a_bad_steam_game_warns()
    {
        var sink = new CollectingSink();
        BasinLog.Sink = sink;
        try
        {
            var log = BasinLog.For("test");
            var table = (TomlTable)Toml.ToModel("""
                steam_game = [570, 440]
                process = "dota2"
                x11_class = "Steam"
                x11_instance = ["steamwebhelper"]
                window_type = ["dialog", "utility"]
                bogus = 1
                """);
            var reader = new TomlReader(table, log);

            Assert.Equal([570u, 440u], WindowRule.SteamGameIds(reader.Raw("steam_game"), reader.Where("steam_game"), log)!);
            Assert.Equal(["dota2"], reader.Words("process")!);
            Assert.Equal(["Steam"], reader.Words("x11_class")!);
            Assert.Equal(["steamwebhelper"], reader.Words("x11_instance")!);
            Assert.Equal(["dialog", "utility"], reader.Words("window_type")!);
            reader.ReportUnknown();
            Assert.Contains(sink.Lines, line => line.Contains("bogus", StringComparison.Ordinal));

            Assert.Null(WindowRule.SteamGameIds(-3L, "steam_game", log));
            Assert.Contains(sink.Lines, line => line.Contains("steam_game wants a positive integer", StringComparison.Ordinal));
        }
        finally
        {
            BasinLog.Sink = null;
        }
    }

    [Fact]
    public void SteamGameId_beats_SteamAppId_in_an_environment()
    {
        Assert.Equal(570u, ProcessIdentity.SteamGameFrom(Encoding.ASCII.GetBytes("HOME=/h\0SteamAppId=440\0SteamGameId=570\0")));
        Assert.Equal(440u, ProcessIdentity.SteamGameFrom(Encoding.ASCII.GetBytes("SteamAppId=440\0PATH=/bin")));
        Assert.Null(ProcessIdentity.SteamGameFrom(Encoding.ASCII.GetBytes("SteamAppId=0\0SteamGameId=x\0")));
    }

    [Fact]
    public void A_wayland_client_with_SteamAppId_in_its_environment_matches_a_steam_game_rule()
    {
        Assert.SkipUnless(PrivateBus.OnPath("weston-simple-shm"), "weston-simple-shm is needed");
        using var host = new CompositorTestHost();
        Surface? surface = null;
        host.Compositor.SurfaceCreated += created => surface ??= created;
        var info = new System.Diagnostics.ProcessStartInfo("weston-simple-shm") { UseShellExecute = false };
        info.Environment["WAYLAND_DISPLAY"] = host.Display.AddSocketAuto();
        info.Environment["SteamAppId"] = "570";
        using var client = System.Diagnostics.Process.Start(info)!;
        try
        {
            XWaylandTestSupport.Pump(host, () => surface is not null);
            var process = ProcessIdentity.Of(surface!);
            var facts = WindowFacts.ForWayland("weston-simple-shm", "simple-shm", false, process);

            Assert.Equal(client.Id, process.Pid);
            Assert.True(new WindowRule { SteamGames = [570] }.Matches(facts));
            Assert.True(new WindowRule { Processes = ["weston-simple-shm"] }.Matches(facts));
        }
        finally
        {
            client.Kill();
            client.WaitForExit(2000);
        }
    }
}
