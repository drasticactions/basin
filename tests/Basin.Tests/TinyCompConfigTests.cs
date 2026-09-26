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
    public void The_background_reads_a_hex_color_and_keeps_the_default_for_anything_else()
    {
        var config = TinyComp.Config.Parse("[compositor]\nbackground = \"#ff8000\"\n", BasinLog.For("t"), out var fatal);
        Assert.Null(fatal);
        Assert.Equal(new Basin.RenderColor(1f, 128 / 255f, 0f, 1f), config.Background);

        var bad = TinyComp.Config.Parse("[compositor]\nbackground = \"orange\"\n", BasinLog.For("t"), out _);
        Assert.Equal(TinyComp.Config.DefaultBackground, bad.Background);
        Assert.Equal(TinyComp.Config.DefaultBackground, TinyComp.Config.Parse(string.Empty, BasinLog.For("t"), out _).Background);
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
        Assert.Equal(TinyComp.FrameStyle.Flat, config.FrameStyle);
        Assert.Equal(0, config.CornerRadius);
        Assert.Equal(14, config.FontSize);
        Assert.Null(config.NightLight);
        Assert.Empty(config.Rules);
        Assert.Equal(9, config.Bindings.Count);
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
    public void The_hypr_section_reads_its_flags_and_shortcut_rows()
    {
        var config = TinyComp.Config.Load(
            Write("""
                [hypr]
                enable = true
                input_capture = false
                ctm = false

                [shortcuts]
                "org.example.app:toggle" = "Super+Shift+p"
                "no-colon" = "Super+q"
                "org.example.app:bad" = "Nope+q"
                """),
            BasinLog.For("t"),
            out var fatal);

        Assert.Null(fatal);
        Assert.True(config.HyprEnabled);
        Assert.False(config.HyprInputCapture);
        Assert.False(config.HyprCtm);
        Assert.Contains("hypr.ctm", config.FromFile);
        var row = Assert.Single(config.Shortcuts);
        Assert.Equal(("org.example.app", "toggle"), row.Key);
        Assert.Equal(Modifiers.Super | Modifiers.Shift, row.Value.Modifiers);
        Assert.Equal(Keysym.FromName("p"), row.Value.Keysym);
        Assert.Contains(_lines, line => line.Contains("no-colon", StringComparison.Ordinal));
    }

    [Fact]
    public void A_named_path_that_does_not_parse_is_fatal()
    {
        _ = TinyComp.Config.Load(Write("[compositor\n"), BasinLog.For("t"), out var fatal);
        Assert.NotNull(fatal);
    }

    [Fact]
    public void Font_size_reads_a_fraction_and_refuses_zero()
    {
        var config = TinyComp.Config.Load(
            Write("[frame]\nfont_size = 12.5\n"), BasinLog.For("t"), out var fatal);

        Assert.Null(fatal);
        Assert.Equal(12.5, config.FontSize);

        config = TinyComp.Config.Load(Write("[frame]\nfont_size = 0\n"), BasinLog.For("t"), out fatal);

        Assert.Null(fatal);
        Assert.Equal(14, config.FontSize);
        Assert.Contains(_lines, line => line.Contains("font_size", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_value_keeps_that_key_default_and_warns()
    {
        var config = TinyComp.Config.Load(
            Write("[frame]\nstyle = \"lozenge\"\ncorner_radius = 12\n"), BasinLog.For("t"), out var fatal);

        Assert.Null(fatal);
        Assert.Equal(TinyComp.FrameStyle.Flat, config.FrameStyle);
        Assert.Equal(12, config.CornerRadius);
        Assert.Contains(_lines, line => line.Contains("style", StringComparison.Ordinal));
    }

    [Fact]
    public void Metacity_keys_parse_and_a_missing_theme_is_fatal()
    {
        var config = TinyComp.Config.Load(
            Write("[frame]\nstyle = \"metacity\"\n[frame.metacity]\ntheme = \"NoSuchThemeAnywhere\"\nbutton_layout = \"close:menu\"\npalette = \"dark\"\n"),
            BasinLog.For("t"),
            out var fatal);
        Assert.Equal(TinyComp.FrameStyle.Metacity, config.FrameStyle);
        Assert.Equal("NoSuchThemeAnywhere", config.MetacityTheme);
        Assert.Equal("close:menu", config.MetacityButtonLayout);
        Assert.Equal("dark", config.MetacityPalette);
        Assert.NotNull(fatal);
        Assert.Contains("NoSuchThemeAnywhere", fatal);
        Assert.Contains("installed themes", fatal);

        _ = TinyComp.Config.Load(Write("[frame]\nstyle = \"metacity\"\n"), BasinLog.For("t"), out var unnamed);
        Assert.NotNull(unnamed);
        Assert.Contains("names no theme", unnamed);

        var flat = TinyComp.Config.Load(Write("[frame]\nstyle = \"flat\"\n[frame.metacity]\ntheme = \"NoSuchThemeAnywhere\"\n"), BasinLog.For("t"), out var ignored);
        Assert.Null(ignored);
        Assert.Equal("NoSuchThemeAnywhere", flat.MetacityTheme);
    }

    [Fact]
    public void The_canvas_table_reads_every_key_and_clamps_the_edge_scale()
    {
        var log = BasinLog.For("t");
        var config = TinyComp.Config.Load(
            Write("[canvas]\nenable = true\nzone = 0.1\nextension = 0.4\nedge_scale = 0.15\nslope = 0.4\nmesh_cell = 8\ngrid = \"drag\"\ngrid_cell = 32\ngrid_color = \"#ff0000\"\nanimation_ms = 100\n"
                + "[output.\"DP-2\"]\nenable = false\nzone = 0.2\n"
                + "[[rule]]\napp_id = \"foot\"\ncanvas = \"right\"\n"),
            log,
            out var fatal);
        Assert.Null(fatal);
        Assert.True(config.Canvas.Enabled);
        Assert.Equal(0.1, config.Canvas.ZoneFraction, 9);
        Assert.Equal(0.4, config.Canvas.ExtensionFraction, 9);
        Assert.Equal(0.15, config.Canvas.EdgeScaleValue, 9);
        Assert.Equal(0.4, config.Canvas.SlopeValue, 9);
        Assert.Equal(8, config.Canvas.MeshCellSize);
        Assert.Equal(TinyComp.CanvasGridMode.Drag, config.Canvas.GridMode);
        Assert.Equal(32, config.Canvas.GridCellSize);
        Assert.Equal(0xff0000ffu, config.Canvas.GridRgba);
        Assert.Equal(100, config.Canvas.AnimationMillis);
        Assert.True(config.CanvasAnywhere);

        var overridden = config.CanvasFor("DP-2", log);
        Assert.False(overridden.Enabled);
        Assert.Equal(0.2, overridden.ZoneFraction, 9);
        Assert.Equal(0.4, overridden.ExtensionFraction, 9);
        Assert.Same(config.Canvas, config.CanvasFor("DP-1", log));
        Assert.Equal(TinyComp.CanvasSide.Right, config.Rules[0].Canvas);

        var clamped = TinyComp.Config.Load(
            Write("[canvas]\nzone = 0.1\nextension = 0.5\nedge_scale = 0.5\n"), log, out _);
        Assert.Equal(0.8 * 0.1 / 0.5, clamped.Canvas.EdgeScaleValue, 6);
        Assert.Contains(_lines, line => line.Contains("edge_scale", StringComparison.Ordinal) && line.Contains("clamping", StringComparison.Ordinal));
        Assert.Equal(TinyComp.KeyAction.ParkLeft, TinyComp.Config.ActionFromName("park-left"));
        Assert.Equal(TinyComp.KeyAction.CanvasToggle, TinyComp.Config.ActionFromName("canvas-toggle"));
    }

    [Fact]
    public void The_overview_table_reads_its_defaults_clamps_and_warns()
    {
        var log = BasinLog.For("t");
        var defaults = TinyComp.Config.Load(Write(string.Empty), log, out _).Overview;
        Assert.True(defaults.Enabled);
        Assert.Equal(0.75, defaults.ScaleValue, 9);
        Assert.Equal(0.667, defaults.ThresholdInValue, 9);
        Assert.Equal(0.333, defaults.ThresholdOutValue, 9);
        Assert.True(defaults.GestureEnabled);
        Assert.Equal(4, defaults.FingerCount);
        Assert.Equal(Basin.Seat.ScreenCorner.TopLeft, defaults.HotCornerValue);
        Assert.Equal(150, defaults.HotCornerMillis);

        var config = TinyComp.Config.Load(
            Write("[overview]\nscale = 0.1\nthreshold_in = 0.2\nthreshold_out = 0.6\ngesture_fingers = 3\n"
                + "hot_corner = \"bottom-right\"\nhot_corner_ms = 300\ngesture = false\n"),
            log,
            out var fatal);
        Assert.Null(fatal);
        var overview = config.Overview;
        Assert.Equal(0.3, overview.ScaleValue, 9);
        Assert.Equal(0.6, overview.ThresholdInValue, 9);
        Assert.Equal(0.2, overview.ThresholdOutValue, 9);
        Assert.Contains(_lines, line => line.Contains("swapping", StringComparison.Ordinal));
        Assert.Equal(4, overview.FingerCount);
        Assert.Contains(_lines, line => line.Contains("workspace swipe", StringComparison.Ordinal));
        Assert.False(overview.GestureEnabled);
        Assert.Equal("bottom-right", overview.HotCornerName);
        Assert.Equal(300, overview.HotCornerMillis);

        var high = TinyComp.Config.Load(Write("[overview]\nscale = 2\ngesture_fingers = 9\nhot_corner = \"middle\"\n"), log, out _).Overview;
        Assert.Equal(0.95, high.ScaleValue, 9);
        Assert.Equal(5, high.FingerCount);
        Assert.Equal(Basin.Seat.ScreenCorner.TopLeft, high.HotCornerValue);
        Assert.Contains(_lines, line => line.Contains("hot_corner \"middle\"", StringComparison.Ordinal));
    }

    [Fact]
    public void The_overview_wall_keys_parse_clamp_and_warn()
    {
        var log = BasinLog.For("t");
        var defaults = TinyComp.Config.Load(Write(string.Empty), log, out _).Overview;
        Assert.Equal(TinyComp.OverviewWall.Slope, defaults.WallValue);
        Assert.Equal(0.04, defaults.WallWidthValue, 9);
        Assert.Equal(0x262a3affu, defaults.WallRgba);
        Assert.Equal(0.25, defaults.WallShadeValue, 9);

        var step = TinyComp.Config.Load(
            Write("[overview]\nwall = \"step\"\nwall_width = 0.5\nwall_color = \"#102030\"\nwall_shade = 2\n"), log, out var fatal);
        Assert.Null(fatal);
        Assert.True(step.Overview.Steps);
        Assert.Equal(0.2, step.Overview.WallWidthValue, 9);
        Assert.Equal(0x102030ffu, step.Overview.WallRgba);
        Assert.Equal(0.8, step.Overview.WallShadeValue, 9);
        var thin = TinyComp.Config.Load(Write("[overview]\nwall_width = 0.0001\nwall_shade = -1\n"), log, out _).Overview;
        Assert.Equal(0.005, thin.WallWidthValue, 9);
        Assert.Equal(0.0, thin.WallShadeValue, 9);

        _lines.Clear();
        var bad = TinyComp.Config.Load(Write("[overview]\nwall = \"cliff\"\n"), log, out _).Overview;
        Assert.Equal(TinyComp.OverviewWall.Slope, bad.WallValue);
        Assert.Contains(_lines, line => line.Contains("wall \"cliff\"", StringComparison.Ordinal));

        var outputs = TinyComp.Config.Load(Write("[output.\"DP-2\"]\noverview_wall = \"step\"\n"), log, out _);
        Assert.True(outputs.OverviewFor("DP-2").Steps);
        Assert.False(outputs.OverviewFor("DP-1").Steps);
        var back = TinyComp.Config.Load(Write("[overview]\nwall = \"step\"\n[output.\"DP-2\"]\noverview_wall = \"slope\"\n"), log, out _);
        Assert.False(back.OverviewFor("DP-2").Steps);
        Assert.True(back.OverviewFor("DP-1").Steps);
    }

    [Fact]
    public void The_overview_texture_keys_parse_clamp_and_resolve()
    {
        var log = BasinLog.For("t");
        var defaults = TinyComp.Config.Load(Write(string.Empty), log, out _).Overview;
        Assert.Equal("none", defaults.WallTextureValue);
        Assert.Equal("none", defaults.ShelfTextureValue);
        Assert.False(defaults.Textured);
        Assert.Equal(1.0, defaults.TextureScaleValue, 9);
        Assert.Equal(0x3a3d44ffu, defaults.ShelfRgba);
        Assert.True(defaults.TextureGridValue);
        Assert.False(TinyComp.Config.Load(Write("[overview]\ntexture_grid = false\n"), log, out _).Overview.TextureGridValue);

        _lines.Clear();
        var step = TinyComp.Config.Load(Write(
            "[overview]\nwall = \"step\"\nwall_texture = \"stone\"\nshelf_texture = \"tiles/floor.png\"\n" +
            "texture_scale = 20\nshelf_color = \"#203040\"\n"), log, out var fatal);
        Assert.Null(fatal);
        Assert.Equal("stone", step.Overview.WallTextureValue);
        Assert.Equal("tiles/floor.png", step.Overview.ShelfTextureValue);
        Assert.True(step.Overview.Textured);
        Assert.Equal(8.0, step.Overview.TextureScaleValue, 9);
        Assert.Equal(0x203040ffu, step.Overview.ShelfRgba);
        Assert.DoesNotContain(_lines, line => line.Contains("apply only", StringComparison.Ordinal));

        var small = TinyComp.Config.Load(Write("[overview]\ntexture_scale = 0.01\nwall_texture = \"\"\n"), log, out _).Overview;
        Assert.Equal(0.25, small.TextureScaleValue, 9);
        Assert.Equal("none", small.WallTextureValue);
        Assert.True(CanvasTextures.TryParse("brick", out _));
        Assert.False(TinyComp.OverviewSetting.IsTexture("none"));
        Assert.True(TinyComp.OverviewSetting.IsTexture("stnoe"));

        var config = Path.Combine(_directory, "tinycomp.toml");
        Assert.Equal(Path.Combine(_directory, "tiles", "floor.png"), TinyComp.OverviewSetting.ResolveTexturePath("tiles/floor.png", config));
        Assert.Equal("/srv/wall.png", TinyComp.OverviewSetting.ResolveTexturePath("/srv/wall.png", config));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, "wall.png"), TinyComp.OverviewSetting.ResolveTexturePath("~/wall.png", config));
        Assert.Equal(Path.GetFullPath("wall.png"), TinyComp.OverviewSetting.ResolveTexturePath("wall.png", null));
    }

    [Fact]
    public void A_texture_key_with_a_slope_wall_warns_once()
    {
        var log = BasinLog.For("t");
        _lines.Clear();
        _ = TinyComp.Config.Load(Write("[overview]\nwall_texture = \"stone\"\nshelf_texture = \"wood\"\n"), log, out _);
        Assert.Single(_lines, line => line.Contains("wall_texture and shelf_texture apply only to wall = \"step\"", StringComparison.Ordinal));

        _lines.Clear();
        _ = TinyComp.Config.Load(Write(
            "[overview]\nwall = \"step\"\nwall_texture = \"stone\"\n[output.\"DP-2\"]\noverview_wall = \"slope\"\n"), log, out _);
        Assert.Single(_lines, line => line.Contains("apply only to wall", StringComparison.Ordinal));

        _lines.Clear();
        _ = TinyComp.Config.Load(Write("[overview]\nwall = \"step\"\nwall_texture = \"stone\"\n"), log, out _);
        _ = TinyComp.Config.Load(Write("[overview]\nwall = \"slope\"\n"), log, out _);
        Assert.DoesNotContain(_lines, line => line.Contains("apply only to wall", StringComparison.Ordinal));
    }

    [Fact]
    public void Step_mode_warns_once_about_the_canvas_keys_it_ignores()
    {
        var log = BasinLog.For("t");
        const string canvas = "[canvas]\nshelf = 0.2\nslope_window = \"flat\"\ncorner = \"square\"\ndrag = \"grid\"\ngrid = \"always\"\n";
        _ = TinyComp.Config.Load(Write(canvas + "[overview]\nwall = \"step\"\n"), log, out _);
        var warnings = _lines.Where(line => line.Contains("ignores [canvas]", StringComparison.Ordinal)).ToList();
        Assert.Single(warnings);
        Assert.Contains("shelf, slope_window, corner, drag = \"grid\"", warnings[0], StringComparison.Ordinal);
        Assert.DoesNotContain("grid = \"always\"", warnings[0], StringComparison.Ordinal);

        _lines.Clear();
        _ = TinyComp.Config.Load(Write(canvas), log, out _);
        Assert.DoesNotContain(_lines, line => line.Contains("ignores [canvas]", StringComparison.Ordinal));

        _lines.Clear();
        _ = TinyComp.Config.Load(Write("[canvas]\ndrag = \"cursor\"\n[output.\"DP-1\"]\nmesh_cell = 8\noverview_wall = \"step\"\n"), log, out _);
        Assert.Contains(_lines, line => line.EndsWith("ignores [canvas] mesh_cell", StringComparison.Ordinal));
    }

    [Fact]
    public void The_overview_warns_against_the_canvas_and_clamps_the_shelf_scale()
    {
        var log = BasinLog.For("t");
        _ = TinyComp.Config.Load(Write("[canvas]\nenable = true\n"), log, out _);
        Assert.Contains(_lines, line => line.Contains("overview is off", StringComparison.Ordinal));

        _lines.Clear();
        var config = TinyComp.Config.Load(Write("[canvas]\nshelf_scale = 0.9\n[overview]\nscale = 0.5\n"), log, out _);
        Assert.Contains(_lines, line => line.Contains("overview clamps it", StringComparison.Ordinal));
        Assert.DoesNotContain(_lines, line => line.Contains("overview is off", StringComparison.Ordinal));
        Assert.Equal(TinyComp.ShelfScales.All(0.5), config.Overview.ShelfScalesFor(config.Canvas));
        Assert.Equal(0.9, config.Canvas.ShelfScaleValues.Left, 9);

        var outputs = TinyComp.Config.Load(
            Write("[overview]\nscale = 0.7\n[output.\"DP-2\"]\noverview = false\noverview_scale = 0.6\n"), log, out var fatal);
        Assert.Null(fatal);
        var second = outputs.OverviewFor("DP-2");
        Assert.False(second.Enabled);
        Assert.Equal(0.6, second.ScaleValue, 9);
        Assert.Same(outputs.Overview, outputs.OverviewFor("DP-1"));
        Assert.Equal(0.7, outputs.OverviewFor("DP-1").ScaleValue, 9);
        Assert.Equal(TinyComp.KeyAction.Overview, TinyComp.Config.ActionFromName("overview"));
        Assert.Equal(TinyComp.KeyAction.ShelveLeft, TinyComp.Config.ActionFromName("shelve-left"));
        Assert.Equal(TinyComp.KeyAction.ShelveBottom, TinyComp.Config.ActionFromName("shelve-bottom"));
        Assert.Equal(TinyComp.KeyAction.Unshelve, TinyComp.Config.ActionFromName("unshelve"));
    }

    [Fact]
    public void The_canvas_sides_list_parses_warns_and_overrides_per_output()
    {
        var log = BasinLog.For("t");
        var defaults = TinyComp.Config.Load(Write("[canvas]\nenable = true\n"), log, out _);
        Assert.Equal(TinyComp.CanvasSide.Left | TinyComp.CanvasSide.Right, defaults.Canvas.SideSet);

        var config = TinyComp.Config.Load(
            Write("[canvas]\nenable = true\nsides = [\"top\", \"left\", \"middle\", \"top\"]\n"
                + "[output.\"DP-2\"]\nsides = [\"bottom\", \"right\"]\n"
                + "[[rule]]\napp_id = \"foot\"\ncanvas = \"bottom-right\"\n"),
            log,
            out var fatal);
        Assert.Null(fatal);
        Assert.Equal(TinyComp.CanvasSide.Top | TinyComp.CanvasSide.Left, config.Canvas.SideSet);
        Assert.Equal("left,top", config.Canvas.SideNames);
        Assert.Contains(_lines, line => line.Contains("sides: \"middle\"", StringComparison.Ordinal));
        Assert.Equal(TinyComp.CanvasSide.Bottom | TinyComp.CanvasSide.Right, config.CanvasFor("DP-2", log).SideSet);
        Assert.Equal(TinyComp.CanvasSide.Top | TinyComp.CanvasSide.Left, config.CanvasFor("DP-1", log).SideSet);
        Assert.Equal(TinyComp.CanvasSide.Bottom | TinyComp.CanvasSide.Right, config.Rules[0].Canvas);

        var empty = TinyComp.Config.Load(Write("[canvas]\nenable = true\nsides = []\n"), log, out _);
        Assert.Equal(TinyComp.CanvasSide.None, empty.Canvas.SideSet);
        Assert.Contains(_lines, line => line.Contains("sides is empty", StringComparison.Ordinal));
        Assert.Equal(TinyComp.KeyAction.ParkUp, TinyComp.Config.ActionFromName("park-up"));
        Assert.Equal(TinyComp.KeyAction.ParkDown, TinyComp.Config.ActionFromName("park-down"));
    }

    [Fact]
    public void The_canvas_window_mode_reads_scale_keys_warns_and_overrides_per_output()
    {
        var log = BasinLog.For("t");
        var defaults = TinyComp.Config.Load(Write("[canvas]\nenable = true\n"), log, out _).Canvas;
        Assert.Equal(TinyComp.CanvasWindowMode.Warp, defaults.WindowMode);
        Assert.Equal(0.35, defaults.MinScaleValue, 9);
        Assert.Equal(1.0, defaults.ScaleReachValue, 9);

        var config = TinyComp.Config.Load(
            Write("[canvas]\nwindow = \"scale\"\nmin_scale = 0.5\nscale_reach = 2.5\n"
                + "[output.\"DP-2\"]\nwindow = \"warp\"\nmin_scale = 0.6\n"),
            log,
            out _);
        Assert.Equal(TinyComp.CanvasWindowMode.Scale, config.Canvas.WindowMode);
        Assert.Equal("scale", config.Canvas.WindowName);
        Assert.Equal(0.5, config.Canvas.MinScaleValue, 9);
        Assert.Equal(2.5, config.Canvas.ScaleReachValue, 9);
        var second = config.CanvasFor("DP-2", log);
        Assert.Equal(TinyComp.CanvasWindowMode.Warp, second.WindowMode);
        Assert.Equal(0.6, second.MinScaleValue, 9);
        Assert.Equal(2.5, second.ScaleReachValue, 9);

        var unknown = TinyComp.Config.Load(Write("[canvas]\nwindow = \"bend\"\n"), log, out _).Canvas;
        Assert.Equal(TinyComp.CanvasWindowMode.Warp, unknown.WindowMode);
        Assert.Contains(_lines, line => line.Contains("window \"bend\" is not warp|scale|terrace, keeping warp", StringComparison.Ordinal));

        var dead = TinyComp.Config.Load(Write("[canvas]\nedge_scale = 0.2\nmin_scale = 0.1\n"), log, out _).Canvas;
        Assert.Equal(0.1, dead.MinScaleValue, 9);
        Assert.Contains(_lines, line => line.Contains("min_scale 0.1 is below edge_scale 0.200 and has no effect", StringComparison.Ordinal));

        var off = TinyComp.Config.Load(Write("[canvas]\nmin_scale = 1\nscale_reach = 40\n"), log, out _).Canvas;
        Assert.Equal(1.0, off.MinScaleValue, 9);
        Assert.Equal(16.0, off.ScaleReachValue, 9);
    }

    [Fact]
    public void The_canvas_terrace_mode_reads_its_shelf_keys_and_its_own_zone_default()
    {
        var log = BasinLog.For("t");
        var defaults = TinyComp.Config.Load(Write("[canvas]\nenable = true\n"), log, out _).Canvas;
        Assert.Equal(0.12, defaults.ZoneFraction, 9);
        Assert.Equal(0.08, defaults.ZoneFractionFor(TinyComp.CanvasWindowMode.Terrace), 9);
        Assert.Equal(0.10, defaults.ShelfFraction, 9);
        Assert.Equal(TinyComp.ShelfScales.All(0.4), defaults.ShelfScaleValues);
        Assert.Equal(0.2, defaults.ShelfMinScaleValue, 9);
        Assert.Equal(0.05, defaults.ShelfStepValue, 9);

        var config = TinyComp.Config.Load(
            Write("[canvas]\nwindow = \"terrace\"\nshelf = 0.12\nshelf_scale = 0.35\nshelf_min_scale = 0.15\nshelf_step = 0.1\n"
                + "[output.\"DP-2\"]\nshelf_scale = { right = 0.3, top = 2.0 }\nshelf = 0.08\nzone = 0.1\n"),
            log,
            out var fatal);
        Assert.Null(fatal);
        var canvas = config.Canvas;
        Assert.Equal(TinyComp.CanvasWindowMode.Terrace, canvas.WindowMode);
        Assert.Equal("terrace", canvas.WindowName);
        Assert.Equal(0.08, canvas.ZoneFraction, 9);
        Assert.Equal(0.12, canvas.ShelfFraction, 9);
        Assert.Equal(TinyComp.ShelfScales.All(0.35), canvas.ShelfScaleValues);
        Assert.Equal(0.15, canvas.ShelfMinScaleValue, 9);
        Assert.Equal(0.1, canvas.ShelfStepValue, 9);

        var second = config.CanvasFor("DP-2", log);
        Assert.Equal(new TinyComp.ShelfScales(0.4, 0.3, 0.9, 0.4), second.ShelfScaleValues);
        Assert.Equal(0.08, second.ShelfFraction, 9);
        Assert.Equal(0.1, second.ZoneFraction, 9);
        Assert.Equal(0.1, second.ZoneFractionFor(TinyComp.CanvasWindowMode.Warp), 9);
        Assert.Equal(0.15, second.ShelfMinScaleValue, 9);
        Assert.Equal("0.40,0.30,0.90,0.40", second.ShelfScaleValues.Names);
        Assert.Equal(TinyComp.ShelfShape.Square, second.ShapeValue);
        var flat = TinyComp.Config.Load(Write("[canvas]\nshelf_shape = \"flat\"\n[output.\"DP-2\"]\nshelf_shape = \"square\"\n"), log, out _);
        Assert.Equal(TinyComp.ShelfShape.Flat, flat.Canvas.ShapeValue);
        Assert.Equal("flat", flat.Canvas.ShapeName);
        Assert.Equal(TinyComp.ShelfShape.Square, flat.CanvasFor("DP-2", log).ShapeValue);
        _ = TinyComp.Config.Load(Write("[canvas]\nshelf_shape = \"round\"\n"), log, out _);
        Assert.Contains(_lines, line => line.Contains("shelf_shape \"round\" is not square|flat", StringComparison.Ordinal));
        Assert.Equal(TinyComp.SlopeWindow.Bend, flat.Canvas.OnSlopeValue);
        var flatSlope = TinyComp.Config.Load(Write("[canvas]\nslope_window = \"flat\"\n[output.\"DP-2\"]\nslope_window = \"bend\"\n"), log, out _);
        Assert.Equal(TinyComp.SlopeWindow.Flat, flatSlope.Canvas.OnSlopeValue);
        Assert.Equal("flat", flatSlope.Canvas.OnSlopeName);
        Assert.Equal(TinyComp.SlopeWindow.Bend, flatSlope.CanvasFor("DP-2", log).OnSlopeValue);
        _ = TinyComp.Config.Load(Write("[canvas]\nslope_window = \"wave\"\n"), log, out _);
        Assert.Contains(_lines, line => line.Contains("slope_window \"wave\" is not bend|flat", StringComparison.Ordinal));
        Assert.Equal(TinyComp.CanvasDrag.Cursor, flat.Canvas.DragValue);
        var grid = TinyComp.Config.Load(Write("[canvas]\ndrag = \"grid\"\n[output.\"DP-2\"]\ndrag = \"cursor\"\n"), log, out _);
        Assert.Equal(TinyComp.CanvasDrag.Grid, grid.Canvas.DragValue);
        Assert.Equal("grid", grid.Canvas.DragName);
        Assert.Equal(TinyComp.CanvasDrag.Cursor, grid.CanvasFor("DP-2", log).DragValue);
        _ = TinyComp.Config.Load(Write("[canvas]\ndrag = \"hand\"\n"), log, out _);
        Assert.Contains(_lines, line => line.Contains("drag \"hand\" is not cursor|grid", StringComparison.Ordinal));
    }

    [Fact]
    public void The_canvas_terrace_keys_warn_about_a_dead_fit_and_a_missing_center()
    {
        var log = BasinLog.For("t");
        var dead = TinyComp.Config.Load(
            Write("[canvas]\nwindow = \"terrace\"\nshelf_scale = { left = 0.3 }\nshelf_min_scale = 0.35\n"), log, out _).Canvas;
        Assert.Equal(0.35, dead.ShelfMinScaleValue, 9);
        Assert.Contains(_lines, line => line.Contains("shelf_min_scale 0.35 is at or above shelf_scale on left", StringComparison.Ordinal));

        var crowded = TinyComp.Config.Load(
            Write("[canvas]\nwindow = \"terrace\"\nshelf = 0.4\nzone = 0.3\n"), log, out _).Canvas;
        Assert.Contains(_lines, line => line.Contains("leave no flat center", StringComparison.Ordinal));
        var (zone, shelf) = crowded.TerraceFractions;
        Assert.Equal(0.45, zone + shelf, 9);
        Assert.Equal(0.3 / 0.4, zone / shelf, 9);

        var badSide = TinyComp.Config.Load(
            Write("[canvas]\nshelf_scale = { middle = 0.5 }\n"), log, out _).Canvas;
        Assert.Equal(TinyComp.ShelfScales.All(0.4), badSide.ShelfScaleValues);
        Assert.Contains(_lines, line => line.Contains("shelf_scale.middle is not left|right|top|bottom", StringComparison.Ordinal));
        Assert.Equal(TinyComp.KeyAction.ShelfSmaller, TinyComp.Config.ActionFromName("shelf-smaller"));
        Assert.Equal(TinyComp.KeyAction.ShelfLarger, TinyComp.Config.ActionFromName("shelf-larger"));
        Assert.Equal(TinyComp.KeyAction.ShelfReset, TinyComp.Config.ActionFromName("shelf-reset"));
        Assert.Equal(TinyComp.KeyAction.CanvasMode, TinyComp.Config.ActionFromName("canvas-mode"));
    }

    [Fact]
    public void The_canvas_corner_takes_a_shape_or_a_radius()
    {
        var log = BasinLog.For("t");
        Assert.Equal(1.0, TinyComp.Config.Load(Write("[canvas]\nenable = true\n"), log, out _).Canvas.CornerRadiusValue);
        Assert.Equal(0.0, TinyComp.Config.Load(Write("[canvas]\ncorner = \"square\"\n"), log, out _).Canvas.CornerRadiusValue);
        Assert.Equal(1.0, TinyComp.Config.Load(Write("[canvas]\ncorner = \"round\"\n"), log, out _).Canvas.CornerRadiusValue);
        Assert.Equal(0.25, TinyComp.Config.Load(Write("[canvas]\ncorner_radius = 0.25\n"), log, out _).Canvas.CornerRadiusValue);
        Assert.Equal(1.0, TinyComp.Config.Load(Write("[canvas]\ncorner_radius = 3\n"), log, out _).Canvas.CornerRadiusValue);

        var both = TinyComp.Config.Load(Write("[canvas]\ncorner = \"square\"\ncorner_radius = 0.5\n"), log, out _);
        Assert.Equal(0.5, both.Canvas.CornerRadiusValue);
        Assert.Contains(_lines, line => line.Contains("corner_radius 0.5 overrides corner", StringComparison.Ordinal));

        _ = TinyComp.Config.Load(Write("[canvas]\ncorner = \"bevel\"\n"), log, out _);
        Assert.Contains(_lines, line => line.Contains("corner \"bevel\" is not round|square|taper", StringComparison.Ordinal));

        var tapered = TinyComp.Config.Load(Write("[canvas]\ncorner = \"taper\"\n"), log, out _).Canvas;
        Assert.True(tapered.CornerTaperValue);
        Assert.Equal(1.0, tapered.CornerRadiusValue);
        Assert.Equal("taper", tapered.CornerName);
        var taperedHalf = TinyComp.Config.Load(Write("[canvas]\ncorner = \"taper\"\ncorner_radius = 0.5\n"), log, out _).Canvas;
        Assert.True(taperedHalf.CornerTaperValue);
        Assert.Equal(0.5, taperedHalf.CornerRadiusValue);
        Assert.DoesNotContain(_lines, line => line.Contains("corner_radius 0.5 overrides corner", StringComparison.Ordinal) && line.Contains("taper", StringComparison.Ordinal));
        var untapered = TinyComp.Config.Load(
            Write("[canvas]\ncorner = \"taper\"\n[output.\"DP-2\"]\ncorner = \"round\"\n"), log, out _);
        Assert.False(untapered.CanvasFor("DP-2", log).CornerTaperValue);
        Assert.True(untapered.CanvasFor("DP-1", log).CornerTaperValue);

        var perOutput = TinyComp.Config.Load(
            Write("[canvas]\nenable = true\n[output.\"DP-2\"]\ncorner = \"square\"\n"), log, out _);
        Assert.Equal(0.0, perOutput.CanvasFor("DP-2", log).CornerRadiusValue);
        Assert.Equal(1.0, perOutput.CanvasFor("DP-1", log).CornerRadiusValue);
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
            Assert.Equal(10, config.Bindings.Count);

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
        Assert.Equal(10, config.Bindings.Count);
        Assert.Empty(config.Rules);
        Assert.Empty(config.Post);
    }
}
