using Basin.Diagnostics;
using Xunit;

namespace MauiComp.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "maui-comp-config-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _lines = [];

    private sealed class ListSink(List<string> lines) : IBasinLogSink
    {
        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            lines.Add($"{level}:{message}");
    }

    public ConfigTests()
    {
        Directory.CreateDirectory(_directory);
        BasinLog.Sink = new ListSink(_lines);
        BasinLog.Level = BasinLogLevel.Trace;
    }

    public void Dispose()
    {
        BasinLog.Sink = null;
        Directory.Delete(_directory, recursive: true);
    }

    private string Write(string text)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".toml");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void The_keys_read_and_an_unknown_one_warns()
    {
        var config = MauiCompConfig.Load(
            Write("theme = \"dark\"\nbackground = \"/tmp/wall.png\"\nbogus = 1\n"), BasinLog.For("t"), out var fatal);

        Assert.Null(fatal);
        Assert.Equal("dark", config.Theme);
        Assert.Equal("/tmp/wall.png", config.Background);
        Assert.Contains("theme", config.FromFile);
        Assert.Contains("background", config.FromFile);
        Assert.Contains(_lines, line => line.Contains("unknown key 'bogus'", StringComparison.Ordinal));
    }

    [Fact]
    public void The_blur_table_defaults_to_the_popups_and_clamps_its_strength()
    {
        var defaults = MauiCompConfig.Load("false", BasinLog.For("t"), out _);
        Assert.Equal(5, defaults.BlurStrength);
        Assert.False(defaults.BlurTaskbar);
        Assert.True(defaults.BlurStartMenu);
        Assert.True(defaults.BlurSwitcher);
        Assert.False(defaults.BlurTitlebars);

        var config = MauiCompConfig.Load(
            Write("[blur]\nstrength = 40\ntaskbar = true\nstart_menu = false\ntitlebars = true\n"), BasinLog.For("t"), out var fatal);
        Assert.Null(fatal);
        Assert.Equal(15, config.BlurStrength);
        Assert.True(config.BlurTaskbar);
        Assert.False(config.BlurStartMenu);
        Assert.True(config.BlurSwitcher);
        Assert.True(config.BlurTitlebars);
        Assert.Contains(_lines, line => line.Contains("[blur] strength", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("gl")]
    [InlineData("pixman")]
    public async Task The_blur_table_reaches_the_chrome_it_names(string renderer)
    {
        var (compositor, _) = HeadlessSessionTests.Require();
        var ssd = HeadlessSessionTests.RequireSsdClient();
        Assert.SkipWhen(renderer != "pixman" && !File.Exists("/dev/dri/renderD128"), "no render node");
        var path = Write("[blur]\nstrength = 4\ntaskbar = true\ntitlebars = true\n");
        using var session = new Session(compositor, renderer, "--config", path);
        var display = await session.DisplayAsync();
        using var framed = Session.StartClient(ssd, display);
        _ = await session.WaitForAsync(line => line.StartsWith("WINDOW \"ssdwin\"", StringComparison.Ordinal), poke: "where");
        await session.SendAsync("launcher");

        if (renderer == "pixman")
        {
            _ = await session.WaitForAsync(line => line.StartsWith("STARTMENU open", StringComparison.Ordinal), poke: "where");
            var unavailable = await session.WaitForAsync(line => line.StartsWith("BLUR ", StringComparison.Ordinal), poke: "where", fresh: true);
            Assert.Contains("BLUR unavailable taskbar=0 startmenu=off switcher=off titlebars=0", unavailable!, StringComparison.Ordinal);
        }
        else
        {
            var blur = await session.WaitForAsync(
                line => line.StartsWith("BLUR ", StringComparison.Ordinal) && line.Contains("startmenu=on", StringComparison.Ordinal),
                poke: "where");
            Assert.NotNull(blur);
            Assert.Contains("strength=4 taskbar=1", blur!, StringComparison.Ordinal);
            Assert.Contains("titlebars=1", blur, StringComparison.Ordinal);
        }

        framed.Kill(entireProcessTree: true);
        await HeadlessSessionTests.AssertCleanExit(session);
    }

    [Fact]
    public void False_reads_nothing_and_a_named_file_that_is_missing_is_fatal()
    {
        var config = MauiCompConfig.Load("false", BasinLog.For("t"), out var fatal);
        Assert.Null(fatal);
        Assert.Equal("light", config.Theme);
        Assert.Null(config.Background);
        Assert.Empty(config.FromFile);

        _ = MauiCompConfig.Load(Path.Combine(_directory, "absent.toml"), BasinLog.For("t"), out fatal);
        Assert.NotNull(fatal);
    }

    [Fact]
    public void A_missing_default_path_is_seeded_with_the_shipped_file()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Path.Combine(_directory, "fresh");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", home);
        try
        {
            var seeded = Path.Combine(home, "maui-comp", "maui-comp.toml");
            var config = MauiCompConfig.Load(null, BasinLog.For("t"), out var fatal);

            Assert.Null(fatal);
            Assert.True(File.Exists(seeded));
            Assert.Equal(MauiCompConfig.Template(), File.ReadAllText(seeded));
            Assert.DoesNotContain(_lines, line => line.Contains("unknown key", StringComparison.Ordinal));
            Assert.Equal("light", config.Theme);

            var written = File.GetLastWriteTimeUtc(seeded);
            _lines.Clear();
            _ = MauiCompConfig.Load(null, BasinLog.For("t"), out _);
            Assert.Equal(written, File.GetLastWriteTimeUtc(seeded));
            Assert.DoesNotContain(_lines, line => line.Contains("wrote the default", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
        }
    }

    [Fact]
    public async Task Sighup_rereads_the_file_and_names_a_key_that_needs_a_restart()
    {
        var (compositor, _) = HeadlessSessionTests.Require();
        var path = Write("theme = \"light\"\n");
        using var session = new Session(compositor, "pixman", "--config", path);
        _ = await session.DisplayAsync();

        File.WriteAllText(path, "theme = \"dark\"\nbackground = \"/tmp/elsewhere.png\"\n");
        using (var hangup = System.Diagnostics.Process.Start("kill", ["-HUP", session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
        {
            await hangup.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        var reload = await session.WaitForAsync(line => line.StartsWith("RELOAD ", StringComparison.Ordinal));
        Assert.NotNull(reload);
        Assert.Contains("theme=dark", reload!, StringComparison.Ordinal);
        Assert.Contains("restart-required=background", reload, StringComparison.Ordinal);

        await HeadlessSessionTests.AssertCleanExit(session);
    }

    [Fact]
    public async Task A_theme_flag_beats_the_key_on_reload_too()
    {
        var (compositor, _) = HeadlessSessionTests.Require();
        var path = Write("theme = \"dark\"\n");
        using var session = new Session(compositor, "pixman", "--config", path, "--theme", "light");
        _ = await session.DisplayAsync();

        using (var hangup = System.Diagnostics.Process.Start("kill", ["-HUP", session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]))
        {
            await hangup.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        var reload = await session.WaitForAsync(line => line.StartsWith("RELOAD ", StringComparison.Ordinal));
        Assert.NotNull(reload);
        Assert.Contains("theme=light", reload!, StringComparison.Ordinal);

        await HeadlessSessionTests.AssertCleanExit(session);
    }
}
