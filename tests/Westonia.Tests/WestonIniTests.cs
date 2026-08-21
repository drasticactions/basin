using Xunit;

namespace Westonia.Tests;

public sealed class WestonIniTests
{
    [Fact]
    public void An_empty_file_keeps_westons_defaults()
    {
        var ini = WestonIni.FromLines([]);

        Assert.Equal(0xFF002244u, ini.Shell.BackgroundColor);
        Assert.Equal(0x90000000u, ini.Shell.PanelColor);
        Assert.Equal(PanelPosition.Top, ini.Shell.PanelPosition);
        Assert.Equal(ClockFormat.Minutes, ini.Shell.ClockFormat);
        Assert.Equal(BackgroundType.Tile, ini.Shell.BackgroundType);
        Assert.True(ini.Shell.Locking);
        Assert.True(ini.Shell.AllowZap);
        Assert.Equal("super", ini.Shell.BindingModifier);
        Assert.Equal(1, ini.Shell.NumWorkspaces);
        Assert.Equal(300, ini.Core.IdleTimeSeconds);
        Assert.True(ini.Core.RequireInput);
        Assert.False(ini.Core.XWayland);
        Assert.Empty(ini.Refusals);
    }

    [Fact]
    public void Comments_and_blank_lines_are_skipped()
    {
        var ini = WestonIni.FromLines([
            "# a comment",
            "; another",
            string.Empty,
            "[shell]",
            "   panel-position=bottom   ",
        ]);

        Assert.Equal(PanelPosition.Bottom, ini.Shell.PanelPosition);
    }

    [Theory]
    [InlineData("top", PanelPosition.Top)]
    [InlineData("bottom", PanelPosition.Bottom)]
    [InlineData("left", PanelPosition.Left)]
    [InlineData("right", PanelPosition.Right)]
    [InlineData("none", PanelPosition.None)]
    public void Every_panel_position_parses(string value, PanelPosition expected)
    {
        var ini = WestonIni.FromLines(["[shell]", $"panel-position={value}"]);

        Assert.Equal(expected, ini.Shell.PanelPosition);
    }

    [Theory]
    [InlineData("minutes", ClockFormat.Minutes)]
    [InlineData("seconds", ClockFormat.Seconds)]
    [InlineData("minutes-24h", ClockFormat.Minutes24H)]
    [InlineData("seconds-24h", ClockFormat.Seconds24H)]
    [InlineData("none", ClockFormat.None)]
    public void Every_clock_format_parses(string value, ClockFormat expected)
    {
        var ini = WestonIni.FromLines(["[shell]", $"clock-format={value}"]);

        Assert.Equal(expected, ini.Shell.ClockFormat);
    }

    [Theory]
    [InlineData("scale", BackgroundType.Scale)]
    [InlineData("scale-crop", BackgroundType.ScaleCrop)]
    [InlineData("scale-fit", BackgroundType.ScaleFit)]
    [InlineData("tile", BackgroundType.Tile)]
    [InlineData("centered", BackgroundType.Centered)]
    public void Every_background_type_parses(string value, BackgroundType expected)
    {
        var ini = WestonIni.FromLines(["[shell]", $"background-type={value}"]);

        Assert.Equal(expected, ini.Shell.BackgroundType);
    }

    [Theory]
    [InlineData("0xff102030", 0xFF102030u)]
    [InlineData("0x102030", 0xFF102030u)]
    [InlineData("#102030", 0xFF102030u)]
    [InlineData("80102030", 0x80102030u)]
    public void Colors_parse_with_and_without_alpha(string value, uint expected)
    {
        var ini = WestonIni.FromLines(["[shell]", $"background-color={value}"]);

        Assert.Equal(expected, ini.Shell.BackgroundColor);
    }

    [Fact]
    public void A_malformed_color_keeps_the_default()
    {
        var ini = WestonIni.FromLines(["[shell]", "background-color=nonsense"]);

        Assert.Equal(0xFF002244u, ini.Shell.BackgroundColor);
    }

    [Fact]
    public void Repeated_launcher_sections_accumulate_in_order()
    {
        var ini = WestonIni.FromLines([
            "[launcher]",
            "icon=/a.png",
            "path=/bin/a",
            "displayname=Alpha",
            "[launcher]",
            "icon=/b.png",
            "path=/bin/b",
            "[launcher]",
            "icon=/c.png",
        ]);

        Assert.Equal(2, ini.Launchers.Count);
        Assert.Equal("Alpha", ini.Launchers[0].DisplayName);
        Assert.Equal("/bin/a", ini.Launchers[0].Path);
        Assert.Null(ini.Launchers[1].DisplayName);
        Assert.Equal("/bin/b", ini.Launchers[1].Path);
    }

    [Fact]
    public void Repeated_output_sections_accumulate_and_need_a_name()
    {
        var ini = WestonIni.FromLines([
            "[output]",
            "name=DP-1",
            "mode=1920x1080",
            "scale=2",
            "transform=90",
            "[output]",
            "mode=800x600",
        ]);

        var output = Assert.Single(ini.Outputs);
        Assert.Equal("DP-1", output.Name);
        Assert.Equal("1920x1080", output.Mode);
        Assert.Equal(2.0, output.Scale);
        Assert.Equal("90", output.Transform);
    }

    [Fact]
    public void The_renderer_comes_from_renderer_or_from_the_use_flags()
    {
        Assert.Equal("gl", WestonIni.FromLines(["[core]", "renderer=gl"]).Core.Renderer);
        Assert.Equal("pixman", WestonIni.FromLines(["[core]", "use-pixman=true"]).Core.Renderer);
        Assert.Equal("vulkan", WestonIni.FromLines(["[core]", "use-vulkan=true"]).Core.Renderer);
        Assert.Null(WestonIni.FromLines(["[core]", "use-gl=false"]).Core.Renderer);
    }

    [Fact]
    public void Modules_and_the_remote_backends_are_refused_by_name()
    {
        var ini = WestonIni.FromLines([
            "[core]",
            "modules=xwayland.so",
            "[rdp]",
            "tls-cert=/etc/cert",
            "[vnc]",
            "port=5900",
            "[pipewire]",
            "output-name=x",
        ]);

        Assert.Contains(ini.Refusals, r => r.Contains("[core] modules", StringComparison.Ordinal));
        Assert.Contains(ini.Refusals, r => r.Contains("[rdp]", StringComparison.Ordinal));
        Assert.Contains(ini.Refusals, r => r.Contains("[vnc]", StringComparison.Ordinal));
        Assert.Contains(ini.Refusals, r => r.Contains("[pipewire]", StringComparison.Ordinal));
        Assert.DoesNotContain(ini.Refusals, r => r.Contains("tls-cert", StringComparison.Ordinal));
    }

    [Fact]
    public void The_legacy_touchpad_triple_is_named_obsolete_and_ignored()
    {
        var ini = WestonIni.FromLines([
            "[touchpad]",
            "constant_accel_factor=50",
            "min_accel_factor=0.16",
            "max_accel_factor=1.0",
        ]);

        Assert.Equal(3, ini.Refusals.Count);
        Assert.All(ini.Refusals, r => Assert.Contains("obsolete", r, StringComparison.Ordinal));
        Assert.Empty(ini.Libinput);
    }

    [Fact]
    public void Mirroring_is_refused_and_named()
    {
        var ini = WestonIni.FromLines(["[output]", "name=DP-2", "mirror-of=DP-1"]);

        Assert.Contains(ini.Refusals, r => r.Contains("mirror-of", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_key_is_reported_rather_than_dropped()
    {
        var ini = WestonIni.FromLines(["[shell]", "invented-key=1"]);

        Assert.Contains(ini.Refusals, r =>
            r.Contains("[shell]", StringComparison.Ordinal) &&
            r.Contains("invented-key", StringComparison.Ordinal));
    }

    [Fact]
    public void The_libinput_section_is_kept_verbatim()
    {
        var ini = WestonIni.FromLines([
            "[libinput]",
            "enable-tap=true",
            "rotation=90",
        ]);

        Assert.Equal("true", ini.Libinput["enable-tap"]);
        Assert.Equal("90", ini.Libinput["rotation"]);
        Assert.Empty(ini.Refusals);
    }

    [Fact]
    public void The_keyboard_section_reaches_the_typed_model()
    {
        var ini = WestonIni.FromLines([
            "[keyboard]",
            "keymap_layout=de",
            "keymap_variant=nodeadkeys",
            "keymap_options=ctrl:nocaps",
            "repeat-rate=25",
            "repeat-delay=300",
            "numlock-on=true",
        ]);

        Assert.Equal("de", ini.Keyboard.Layout);
        Assert.Equal("nodeadkeys", ini.Keyboard.Variant);
        Assert.Equal("ctrl:nocaps", ini.Keyboard.Options);
        Assert.Equal(25, ini.Keyboard.RepeatRate);
        Assert.Equal(300, ini.Keyboard.RepeatDelay);
        Assert.True(ini.Keyboard.NumlockOn);
    }

    [Fact]
    public void Workspaces_are_clamped_to_a_usable_range()
    {
        Assert.Equal(1, WestonIni.FromLines(["[shell]", "num-workspaces=0"]).Shell.NumWorkspaces);
        Assert.Equal(6, WestonIni.FromLines(["[shell]", "num-workspaces=6"]).Shell.NumWorkspaces);
        Assert.Equal(32, WestonIni.FromLines(["[shell]", "num-workspaces=900"]).Shell.NumWorkspaces);
    }

    [Fact]
    public void A_missing_file_yields_defaults_and_no_path()
    {
        var ini = WestonIni.Load("/nonexistent/weston.ini");

        Assert.Null(ini.Path);
        Assert.Equal(PanelPosition.Top, ini.Shell.PanelPosition);
    }
}
