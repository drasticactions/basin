using Basin.Config;
using Basin.Effects;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class TinyCompConfigTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "tinycomp-config-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _lines = [];

    private sealed class ListSink(List<string> lines) : IBasinLogSink
    {
        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            lines.Add($"{level}:{message}");
    }

    public TinyCompConfigTests()
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
    public void No_file_leaves_every_default_and_seeds_the_built_in_bindings()
    {
        var config = TinyComp.Config.Load("false", BasinLog.For("t"), out var fatal);

        Assert.Null(fatal);
        Assert.Equal("vulkan", config.Renderer);
        Assert.Equal(1, config.Outputs);
        Assert.True(config.Transactions);
        Assert.True(config.Offload);
        Assert.Equal(TinyComp.FrameStyle.Beos, config.FrameStyle);
        Assert.Equal(0, config.CornerRadius);
        Assert.Null(config.NightLight);
        Assert.Empty(config.Rules);
        Assert.Equal(8, config.Bindings.Count);
        Assert.Contains(config.Bindings, b =>
            b.Action == TinyComp.KeyAction.Quit
            && b.ModifierMask == Modifiers.Alt
            && b.Keysym == Keysym.FromName("Escape"));
    }

    [Fact]
    public void A_named_path_that_is_missing_is_fatal_and_a_missing_default_is_not()
    {
        _ = TinyComp.Config.Load(Path.Combine(_directory, "absent.toml"), BasinLog.For("t"), out var fatal);
        Assert.NotNull(fatal);
        Assert.Contains("absent.toml", fatal, StringComparison.Ordinal);

        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_directory, "empty"));
        try
        {
            var config = TinyComp.Config.Load(null, BasinLog.For("t"), out var quiet);
            Assert.Null(quiet);
            Assert.Equal("vulkan", config.Renderer);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
        }
    }

    [Fact]
    public void A_named_path_that_does_not_parse_is_fatal()
    {
        _ = TinyComp.Config.Load(Write("[compositor\n"), BasinLog.For("t"), out var fatal);
        Assert.NotNull(fatal);
    }

    [Fact]
    public void A_bad_value_keeps_that_key_default_and_warns()
    {
        var config = TinyComp.Config.Load(
            Write("[frame]\nstyle = \"lozenge\"\ncorner_radius = 12\n"), BasinLog.For("t"), out var fatal);

        Assert.Null(fatal);
        Assert.Equal(TinyComp.FrameStyle.Beos, config.FrameStyle);
        Assert.Equal(12, config.CornerRadius);
        Assert.Contains(_lines, line => line.Contains("style", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_key_warns()
    {
        _ = TinyComp.Config.Load(Write("[compositor]\nrenderrer = \"gl\"\n"), BasinLog.For("t"), out _);
        Assert.Contains(_lines, line => line.Contains("unknown key 'compositor.renderrer'", StringComparison.Ordinal));
    }

    [Fact]
    public void Bindings_merge_over_the_defaults_and_can_unbind()
    {
        var config = TinyComp.Config.Load(
            Write("""
                [bindings]
                "Alt+Escape" = false
                "Super+q" = "quit"
                "Alt+Return" = { exec = "foot" }
                """),
            BasinLog.For("t"),
            out _);

        Assert.DoesNotContain(config.Bindings, b =>
            b.ModifierMask == Modifiers.Alt && b.Keysym == Keysym.FromName("Escape"));
        Assert.Contains(config.Bindings, b =>
            b.ModifierMask == Modifiers.Super && b.Action == TinyComp.KeyAction.Quit);
        Assert.Contains(config.Bindings, b => b.Command is ["foot"]);
        Assert.Contains(config.Bindings, b => b.Action == TinyComp.KeyAction.CycleScale);
    }

    [Fact]
    public void The_most_specific_rule_wins_and_supplies_every_setting()
    {
        var config = TinyComp.Config.Load(
            Write("""
                [[rule]]
                app_id = "mpv"
                frame = "flat"
                workspace = 3

                [[rule]]
                app_id = "mpv"
                title_regex = "holiday"
                frame = "none"
                x = 111
                y = 222
                """),
            BasinLog.For("t"),
            out _);

        Assert.Equal(2, config.Rules.Count);

        var specific = config.RuleFor("mpv", "holiday.mkv");
        Assert.Equal(TinyComp.FrameStyle.None, specific!.FrameStyle);
        Assert.Equal(111, specific.X);
        Assert.Null(specific.Workspace);

        var general = config.RuleFor("mpv", "work.mkv");
        Assert.Equal(TinyComp.FrameStyle.Flat, general!.FrameStyle);
        Assert.Equal(3, general.Workspace);

        Assert.Null(config.RuleFor("firefox", "holiday.mkv"));
    }

    [Fact]
    public void A_rule_naming_no_match_criteria_is_dropped_with_a_warning()
    {
        var config = TinyComp.Config.Load(Write("[[rule]]\nframe = \"none\"\n"), BasinLog.For("t"), out _);

        Assert.Empty(config.Rules);
        Assert.Contains(_lines, line => line.Contains("[[rule]]", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_rule_pattern_warns_and_drops_the_rule()
    {
        var config = TinyComp.Config.Load(
            Write("[[rule]]\ntitle_regex = \"([\"\n"), BasinLog.For("t"), out _);

        Assert.Empty(config.Rules);
        Assert.Contains(_lines, line => line.Contains("is invalid", StringComparison.Ordinal));
    }

    [Fact]
    public void Icc_without_a_profile_falls_back_to_edid()
    {
        var config = TinyComp.Config.Load(Write("[color]\nsource = \"icc\"\n"), BasinLog.For("t"), out _);

        Assert.Equal(Basin.Capabilities.OutputColorProfileSource.Edid, config.ColorSource);
        Assert.Contains(_lines, line => line.Contains("names no profile", StringComparison.Ordinal));
    }

    [Fact]
    public void Effects_read_their_whole_section()
    {
        var config = TinyComp.Config.Load(
            Write("""
                [effects]
                wobbly = true
                open = "glide"
                close = "fall-apart"
                minimize = "magic-lamp"
                switcher = true
                highlight = true
                dim_inactive = true
                drop_shadow = true
                slide_back = true
                stretch = true
                notifications = true
                shake_cursor = true
                mouse_click = true
                mouse_mark = true
                track_mouse = true
                touch_points = true
                system_bell = true
                blend_changes = true
                screen_transform = true
                startup_feedback = "bouncing"
                color_blindness = "tritanopia"
                color_blindness_intensity = 0.5
                zoom_tracking = "centered-strict"
                post = ["show-paint", "zoom"]
                """),
            BasinLog.For("t"),
            out _);

        Assert.True(config.Wobbly);
        Assert.Equal("glide", config.OpenAnimation);
        Assert.Equal("fall-apart", config.CloseAnimation);
        Assert.Equal("magic-lamp", config.MinimizeAnimation);
        Assert.True(config.Switcher);
        Assert.True(config.Highlight);
        Assert.True(config.DimInactive);
        Assert.True(config.DropShadow);
        Assert.True(config.SlideBack);
        Assert.True(config.Stretch);
        Assert.True(config.Notifications);
        Assert.True(config.ShakeCursor);
        Assert.True(config.MouseClick);
        Assert.True(config.MouseMark);
        Assert.True(config.TrackMouse);
        Assert.True(config.TouchPoints);
        Assert.True(config.SystemBell);
        Assert.True(config.BlendChanges);
        Assert.True(config.ScreenTransform);
        Assert.Equal(StartupFeedbackKind.Bouncing, config.StartupFeedback);
        Assert.Equal(ColorBlindnessMode.Tritanopia, config.ColorBlindness);
        Assert.Equal(0.5, config.ColorBlindnessIntensity, 6);
        Assert.Equal(ZoomTracking.CenteredStrict, config.ZoomTracking);
        Assert.Equal(["show-paint", "zoom"], config.Post);
    }

    [Fact]
    public void Every_effect_is_off_by_default()
    {
        var config = TinyComp.Config.Load("false", BasinLog.For("t"), out _);

        Assert.False(config.Wobbly);
        Assert.Null(config.OpenAnimation);
        Assert.Null(config.CloseAnimation);
        Assert.Null(config.MinimizeAnimation);
        Assert.False(config.Switcher);
        Assert.False(config.Highlight);
        Assert.False(config.DimInactive);
        Assert.False(config.DropShadow);
        Assert.False(config.SlideBack);
        Assert.False(config.Stretch);
        Assert.False(config.Notifications);
        Assert.False(config.ShakeCursor);
        Assert.False(config.MouseClick);
        Assert.False(config.MouseMark);
        Assert.False(config.TrackMouse);
        Assert.False(config.TouchPoints);
        Assert.False(config.SystemBell);
        Assert.False(config.BlendChanges);
        Assert.False(config.ScreenTransform);
        Assert.Equal(StartupFeedbackKind.None, config.StartupFeedback);
        Assert.Empty(config.Post);
    }

    [Fact]
    public void A_single_post_name_reads_as_a_one_stage_list_and_none_is_empty()
    {
        Assert.Equal(
            ["magnify"],
            TinyComp.Config.Load(Write("[effects]\npost = \"magnify\"\n"), BasinLog.For("t"), out _).Post);

        Assert.Empty(
            TinyComp.Config.Load(Write("[effects]\npost = \"none\"\n"), BasinLog.For("t"), out _).Post);
    }

    [Fact]
    public void An_unknown_post_stage_warns_and_the_rest_of_the_list_stands()
    {
        var config = TinyComp.Config.Load(
            Write("[effects]\npost = [\"invert\", \"sepia\", \"show-paint\"]\n"), BasinLog.For("t"), out _);

        Assert.Equal(["invert", "show-paint"], config.Post);
        Assert.Contains(_lines, line => line.Contains("unknown stage 'sepia'", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_animation_keeps_the_default_and_warns()
    {
        var config = TinyComp.Config.Load(
            Write("[effects]\nclose = \"dissolve\"\nminimize = \"genie\"\n"), BasinLog.For("t"), out _);

        Assert.Null(config.CloseAnimation);
        Assert.Null(config.MinimizeAnimation);
        Assert.Contains(_lines, line => line.Contains("effects.close", StringComparison.Ordinal));
        Assert.Contains(_lines, line => line.Contains("effects.minimize", StringComparison.Ordinal));
    }

    [Fact]
    public void The_file_names_no_backend()
    {
        var config = TinyComp.Config.Load(
            Write("[compositor]\nbackend = \"nested\"\n"), BasinLog.For("t"), out var fatal);

        Assert.Null(fatal);
        Assert.Contains(_lines, line => line.Contains("unknown key 'compositor.backend'", StringComparison.Ordinal));
        Assert.DoesNotContain("Backend", typeof(TinyComp.Config).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void A_key_the_file_omits_keeps_the_options_own_default()
    {
        var command = new System.CommandLine.RootCommand("test");
        var option = new System.CommandLine.Option<string>("--renderer")
        {
            DefaultValueFactory = _ => "from-the-environment",
        };
        command.Options.Add(option);
        var parsed = command.Parse([]);

        Assert.Equal(
            "from-the-environment",
            Basin.Cli.BasinCommand.Effective(parsed, option, "from-the-config", configured: false));
        Assert.Equal(
            "from-the-config",
            Basin.Cli.BasinCommand.Effective(parsed, option, "from-the-config", configured: true));

        var given = command.Parse(["--renderer", "from-the-flag"]);
        Assert.Equal(
            "from-the-flag",
            Basin.Cli.BasinCommand.Effective(given, option, "from-the-config", configured: true));
    }

    [Fact]
    public void The_file_records_which_shared_keys_it_set()
    {
        var config = TinyComp.Config.Load(
            Write("[compositor]\noutputs = 2\n"), BasinLog.For("t"), out _);

        Assert.Contains("outputs", config.FromFile);
        Assert.DoesNotContain("renderer", config.FromFile);
        Assert.DoesNotContain("scale", config.FromFile);
    }

    [Fact]
    public void A_missing_default_path_is_seeded_with_the_shipped_example()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Path.Combine(_directory, "fresh");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", home);
        try
        {
            var seeded = Path.Combine(home, "tinycomp", "tinycomp.toml");
            Assert.False(File.Exists(seeded));

            var config = TinyComp.Config.Load(null, BasinLog.For("t"), out var fatal);

            Assert.Null(fatal);
            Assert.True(File.Exists(seeded));
            Assert.Equal(TinyComp.Config.Template(), File.ReadAllText(seeded));
            Assert.Contains(_lines, line => line.Contains("wrote the default", StringComparison.Ordinal));
            Assert.DoesNotContain(_lines, line => line.Contains("unknown key", StringComparison.Ordinal));
            Assert.Equal(9, config.Bindings.Count);

            var written = File.GetLastWriteTimeUtc(seeded);
            _lines.Clear();
            _ = TinyComp.Config.Load(null, BasinLog.For("t"), out _);
            Assert.Equal(written, File.GetLastWriteTimeUtc(seeded));
            Assert.DoesNotContain(_lines, line => line.Contains("wrote the default", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
        }
    }

    [Fact]
    public void An_output_section_pins_scale_transform_and_mode_by_connector_name()
    {
        var path = Write("""
            [output."DP-1"]
            scale     = 1.5
            transform = "270"
            mode      = "3840x2560@60"

            [output."HDMI-A-1"]
            scale = 2
            """);

        var config = TinyComp.Config.Load(path, BasinLog.For("t"), out var fatal);

        Assert.Null(fatal);
        Assert.DoesNotContain(_lines, line => line.Contains("unknown key", StringComparison.Ordinal));
        var setting = config.OutputSettingFor("DP-1");
        Assert.NotNull(setting);
        Assert.Equal(1.5, setting.Scale);
        Assert.Equal(OutputTransform.Rotate270, setting.Transform);
        Assert.Equal((3840, 2560, (int?)60), setting.Mode);
        Assert.Equal(2, config.OutputSettingFor("HDMI-A-1")!.Scale);
        Assert.Null(config.OutputSettingFor("DP-2"));
    }

    [Fact]
    public void An_output_section_warns_on_a_bad_value_and_keeps_the_rest()
    {
        var path = Write("""
            [output."DP-1"]
            scale     = 1.25
            transform = "diagonal"
            mode      = "wide"
            """);

        var config = TinyComp.Config.Load(path, BasinLog.For("t"), out var fatal);

        Assert.Null(fatal);
        Assert.Contains(_lines, line => line.Contains("transform", StringComparison.Ordinal));
        Assert.Contains(_lines, line => line.Contains("mode", StringComparison.Ordinal));
        var setting = config.OutputSettingFor("DP-1");
        Assert.NotNull(setting);
        Assert.Equal(1.25, setting.Scale);
        Assert.Null(setting.Transform);
        Assert.Null(setting.Mode);
    }

    [Fact]
    public void Reading_no_file_and_naming_one_never_seed()
    {
        var previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Path.Combine(_directory, "untouched");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", home);
        try
        {
            _ = TinyComp.Config.Load("false", BasinLog.For("t"), out _);
            Assert.False(Directory.Exists(home));

            _ = TinyComp.Config.Load(Path.Combine(_directory, "absent.toml"), BasinLog.For("t"), out var fatal);
            Assert.NotNull(fatal);
            Assert.False(Directory.Exists(home));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous);
        }
    }

    [Fact]
    public void The_new_actions_have_names()
    {
        Assert.Equal(TinyComp.KeyAction.ZoomIn, TinyComp.Config.ActionFromName("zoom-in"));
        Assert.Equal(TinyComp.KeyAction.ZoomOut, TinyComp.Config.ActionFromName("zoom-out"));
        Assert.Equal(TinyComp.KeyAction.ZoomReset, TinyComp.Config.ActionFromName("zoom-reset"));
        Assert.Equal(TinyComp.KeyAction.MarkUndo, TinyComp.Config.ActionFromName("mark-undo"));
        Assert.Equal(TinyComp.KeyAction.MarkClear, TinyComp.Config.ActionFromName("mark-clear"));
        Assert.Equal(TinyComp.KeyAction.Bell, TinyComp.Config.ActionFromName("bell"));
        Assert.Null(TinyComp.Config.ActionFromName("zoom-sideways"));
    }

    [Fact]
    public void The_shipped_example_parses_with_every_key_read()
    {
        var example = Write(TinyComp.Config.Template());
        var config = TinyComp.Config.Load(example, BasinLog.For("t"), out var fatal);

        Assert.Null(fatal);
        Assert.DoesNotContain(_lines, line => line.Contains("unknown key", StringComparison.Ordinal));
        Assert.DoesNotContain(_lines, line => line.Contains("unknown stage", StringComparison.Ordinal));
        Assert.Equal(9, config.Bindings.Count);
        Assert.Empty(config.Rules);
        Assert.Empty(config.Post);
    }
}
