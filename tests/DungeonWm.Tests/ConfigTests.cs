using Basin.Diagnostics;
using Xunit;

namespace DungeonWm.Tests;

public sealed class ConfigTests
{
    private sealed class CollectingSink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            Lines.Add($"{level}:{message}");
    }

    [Fact]
    public void The_takeover_table_sets_the_scaler_filter_and_frame_limit()
    {
        var defaults = DungeonWmConfig.Parse("", BasinLog.For("test"));
        Assert.Null(defaults.TakeoverScaler);
        Assert.Null(defaults.TakeoverFilter);
        Assert.Equal(0, defaults.TakeoverFrameLimit);

        var settings = DungeonWmConfig.Parse("[takeover]\nscaler = \"integer\"\nfilter = \"pixel\"\nframe_limit = 30\n", BasinLog.For("test"));
        Assert.Equal(Basin.Scene.SceneFitMode.Integer, settings.TakeoverScaler);
        Assert.Equal(Basin.TextureFilter.Pixel, settings.TakeoverFilter);
        Assert.Equal(30, settings.TakeoverFrameLimit);
    }

    [Fact]
    public void A_spawn_runs_through_the_shell_and_an_array_is_an_argv()
    {
        var settings = DungeonWmConfig.Parse("[keybindings]\n\"Super+Return\" = \"spawn foot -e 'a b'\"\n\"Super+T\" = [\"foot\", \"-e\", \"htop\"]\n\"Super+K\" = \"takeover\"\n", BasinLog.For("test"));
        var bindings = new DungeonWmBindings(settings.Bindings, BasinLog.For("test"));
        var enter = Xkb.XkbKeysym.FromName("Return").Value;
        var t = new Xkb.XkbKeysym('t').Value;
        var k = new Xkb.XkbKeysym('k').Value;

        Assert.Equal(["/bin/sh", "-c", "foot -e 'a b'"], bindings.Match(enter, Basin.Config.Modifiers.Super)!.Command!);
        Assert.Equal(["foot", "-e", "htop"], bindings.Match(t, Basin.Config.Modifiers.Super)!.Command!);
        Assert.Equal("takeover", bindings.Match(k, Basin.Config.Modifiers.Super)!.Action);
        Assert.Null(bindings.Match(k, Basin.Config.Modifiers.Super | Basin.Config.Modifiers.Mod5));
    }

    [Fact]
    public void A_spawn_binding_is_an_action_and_a_bare_spawn_warns()
    {
        var sink = new CollectingSink();
        var level = BasinLog.Level;
        BasinLog.Sink = sink;
        BasinLog.Level = BasinLogLevel.Trace;
        try
        {
            var settings = DungeonWmConfig.Parse("[keybindings]\n\"Super+Return\" = \"spawn fuzzel\"\n\"Super+D\" = \"spawn\"\n\"Super+E\" = \"spawn   \"\n", BasinLog.For("test"));
            Assert.Equal("spawn fuzzel", settings.Bindings["Super+Return"]);
            Assert.False(settings.Bindings.ContainsKey("Super+D"));
            Assert.False(settings.Bindings.ContainsKey("Super+E"));
            Assert.True(sink.Lines.Any(line => line.Contains("Super+D: unknown action spawn", StringComparison.Ordinal)), string.Join(" | ", sink.Lines));
            Assert.True(DungeonWmBindings.IsAction("spawn foot --server"));
            Assert.False(DungeonWmBindings.IsAction("spawner"));
        }
        finally
        {
            BasinLog.Sink = null;
            BasinLog.Level = level;
        }
    }

    [Fact]
    public void A_letter_binding_matches_the_unshifted_key_whatever_its_case()
    {
        var bindings = new DungeonWmBindings(new DungeonWmSettings().Bindings, BasinLog.For("test"));
        Assert.Equal("close", bindings.Match(Basin.Config.Keysym.FromName("q"), Basin.Config.Modifiers.Super)?.Action);
        Assert.Equal("takeover", bindings.Match(Basin.Config.Keysym.FromName("f"), Basin.Config.Modifiers.Super)?.Action);
        Assert.Null(bindings.Match(Basin.Config.Keysym.FromName("Q"), Basin.Config.Modifiers.Super));
    }

    [Fact]
    public void The_crosshair_keys_parse_and_a_table_without_use_still_gets_e()
    {
        var defaults = new DungeonWmSettings();
        var seeded = DungeonWmConfig.Parse(DungeonWmConfig.Template(), BasinLog.For("test"));
        Assert.Null(seeded.CrosshairOutput);
        Assert.Equal(defaults.Reach, seeded.Reach);
        Assert.Equal(defaults.ReticleColor, seeded.ReticleColor);
        Assert.Equal(defaults.ReticleSize, seeded.ReticleSize);
        Assert.Equal("use", seeded.Bindings["E"]);
        Assert.Equal("release", seeded.Bindings["Super+E"]);
        Assert.Equal("rotate", seeded.Bindings["R"]);
        Assert.Equal("square", seeded.Bindings["Shift+R"]);

        var named = DungeonWmConfig.Parse("[camera]\ncrosshair_output = \"DP-2\"\nreach = 2.5\n\n[reticle]\ncolor = \"#ff000080\"\nsize = 32\n", BasinLog.For("test"));
        Assert.Equal("DP-2", named.CrosshairOutput);
        Assert.Equal(2.5f, named.Reach);
        Assert.Equal(32, named.ReticleSize);
        Assert.Equal(1, named.ReticleColor.X);
        Assert.Equal(0, named.ReticleColor.Y);

        var e = Basin.Config.Keysym.FromName("e");
        var bare = new DungeonWmBindings(DungeonWmConfig.Parse("[keybindings]\n\"Super+Q\" = \"close\"\n", BasinLog.For("test")).Bindings, BasinLog.For("test"));
        Assert.Equal("use", bare.Match(e, Basin.Config.Modifiers.None)?.Action);
        Assert.Equal("rotate", bare.Match(Basin.Config.Keysym.FromName("r"), Basin.Config.Modifiers.None)?.Action);
        Assert.Equal("square", bare.Match(Basin.Config.Keysym.FromName("r"), Basin.Config.Modifiers.Shift)?.Action);
        var moved = new DungeonWmBindings(DungeonWmConfig.Parse("[keybindings]\n\"U\" = \"use\"\n", BasinLog.For("test")).Bindings, BasinLog.For("test"));
        Assert.Null(moved.Match(e, Basin.Config.Modifiers.None));
        Assert.Equal("use", moved.Match(Basin.Config.Keysym.FromName("u"), Basin.Config.Modifiers.None)?.Action);
    }

    [Fact]
    public void The_defaults_open_fuzzel_and_animate_for_600_ms()
    {
        var defaults = new DungeonWmSettings();
        Assert.Equal("spawn fuzzel", defaults.Bindings["Super+Return"]);
        Assert.Equal(600, defaults.OpenMilliseconds);
        Assert.Equal(600, defaults.CloseMilliseconds);

        var seeded = DungeonWmConfig.Parse(DungeonWmConfig.Template(), BasinLog.For("test"));
        Assert.Equal("spawn fuzzel", seeded.Bindings["Super+Return"]);
        Assert.Equal(600, seeded.OpenMilliseconds);
        Assert.Equal(600, seeded.CloseMilliseconds);

        var off = DungeonWmConfig.Parse("[windows]\nopen_ms = 0\nclose_ms = 0\n", BasinLog.For("test"));
        Assert.Equal((0, 0), (off.OpenMilliseconds, off.CloseMilliseconds));
    }
}
