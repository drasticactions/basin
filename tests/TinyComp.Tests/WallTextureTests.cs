using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Basin.Effects;
using Basin;
using Xunit;
using static TinyComp.Tests.CanvasRows;

namespace TinyComp.Tests;

public sealed class WallTextureTests
{
    [Fact]
    public void A_preset_wall_texture_is_reported_and_drawn()
    {
        double flatSpread;
        using (var flat = CanvasSession.Start(TexturedStep(string.Empty), SsdWin()))
        {
            Assert.SkipWhen(flat is null, "tinycomp or ssdwin is not available beside the tests");
            var plain = OpenTextured(flat!);
            Assert.Equal("none", plain["wall-texture"]);
            Assert.Equal("none", plain["shelf-texture"]);
            flatSpread = WallSpread(flat!.Shot("flat"));
        }

        using var session = CanvasSession.Start(TexturedStep("wall_texture = \"stone\"\n"), SsdWin());
        Assert.NotNull(session);
        var overview = OpenTextured(session!);
        Assert.Equal("stone", overview["wall-texture"]);
        Assert.Equal("none", overview["shelf-texture"]);
        session!.Send("where");
        var where = session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true", 5000);
        Assert.NotNull(where);
        Assert.Contains(" wall=step wall-texture=stone shelf-texture=none", where, StringComparison.Ordinal);
        var stoneSpread = WallSpread(session!.Shot("stone"));
        Assert.True(stoneSpread > flatSpread + 4, $"the wall varies {stoneSpread} textured and {flatSpread} flat");
    }

    [Fact]
    public void Texture_grid_false_turns_off_every_grid_line_while_a_texture_is_set()
    {
        const string grid =
            "[overview]\ngrid = \"always\"\ndesktop_grid = true\nanimation_ms = 100\nwall = \"step\"\nwall_texture = \"stone\"\n";
        int withGrid;
        using (var shown = CanvasSession.Start(grid, SsdWin()))
        {
            Assert.SkipWhen(shown is null, "tinycomp or ssdwin is not available beside the tests");
            _ = OpenTextured(shown!);
            withGrid = BlueGridPixels(shown!.Shot("grid"));
        }

        using var hidden = CanvasSession.Start(grid + "texture_grid = false\n", SsdWin());
        Assert.NotNull(hidden);
        _ = OpenTextured(hidden!);
        var without = BlueGridPixels(hidden!.Shot("nogrid"));
        Assert.True(withGrid > 10000, $"the grid drew {withGrid} pixels");
        Assert.True(without < 200, $"{without} grid pixels are left");

        hidden.Rewrite(grid.Replace("wall_texture = \"stone\"\n", string.Empty, StringComparison.Ordinal) + "texture_grid = false\n");
        hidden.Send("reload");
        Assert.NotNull(hidden.WaitForLine("RELOAD "));
        Assert.True(BlueGridPixels(hidden.Shot("flat")) > 10000, "with no texture the grid comes back");
    }

    [Fact]
    public void A_terrace_leaves_its_desktop_clear_until_desktop_grid_is_set()
    {
        using var session = CanvasSession.Start(BareTerrace, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        AssertBarePlateau(session!, BareTerrace, 260, 0, 1020, 720, () => { });
    }

    [Theory]
    [InlineData("slope")]
    [InlineData("step")]
    public void Overview_leaves_its_zoomed_desktop_clear_until_desktop_grid_is_set(string wall)
    {
        var config = $"[overview]\ngrid = \"always\"\nanimation_ms = 100\nwall = \"{wall}\"\n";
        using var session = CanvasSession.Start(config, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        AssertBarePlateau(session!, config, 200, 110, 1080, 610, () =>
        {
            session!.Send("overview open");
            session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000);
        });
    }

    [Fact]
    public void The_options_line_reports_desktop_grid()
    {
        using (var plain = CanvasSession.Start(BareTerrace, SsdWin()))
        {
            Assert.SkipWhen(plain is null, "tinycomp or ssdwin is not available beside the tests");
            Assert.SkipWhen(plain!.Options is null, "a Release tinycomp prints no OPTIONS line");
            Assert.Contains(" canvas-desktop-grid=off", plain.Options, StringComparison.Ordinal);
        }

        using var lined = CanvasSession.Start(BareTerrace + "desktop_grid = true\n", SsdWin());
        Assert.NotNull(lined);
        Assert.Contains(" canvas-desktop-grid=on", lined!.Options, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_table_turns_the_desktop_grid_on_for_that_output_only()
    {
        using var session = CanvasSession.Start(
            BareTerrace + "[output.\"HEADLESS-1\"]\ndesktop_grid = true\n", SsdWin(), compositorArguments: ["--outputs", "2"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var (_, _, window, _, _) = session!.CanvasWindow();
        var first = GridPixelsIn(session.Shot("first", 0), 260, 0, 1020, 720, window);
        var second = GridPixelsIn(session.Shot("second", 1), 260, 0, 1020, 720, default);
        Assert.True(first > 3000, $"HEADLESS-1 drew {first} grid pixels on its desktop");
        Assert.True(second < 200, $"HEADLESS-2 drew {second} grid pixels on its desktop");
    }

    [Fact]
    public void A_wallpaper_fills_the_desktop_between_the_seams_until_it_is_asked_for_the_output()
    {
        const string config = "[canvas]\nenable = true\nwindow = \"terrace\"\ngrid = \"never\"\nanimation_ms = 100\n";
        using var session = CanvasSession.Start(config, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        using var wallpaper = new TestWallpaper(session!.SocketPath, 0xFFC02020);
        wallpaper.Pump();
        var (first, last) = RedSpan(session.Shot("desktop"), 700);
        Assert.InRange(first, 228, 232);
        Assert.InRange(last, 1047, 1051);

        session.Rewrite(config + "wallpaper = \"output\"\n");
        session.Send("reload");
        Assert.NotNull(session.WaitForLine("RELOAD "));
        wallpaper.Pump();
        Assert.Equal((0, 1279), RedSpan(session.Shot("output"), 700));
    }

    [Fact]
    public void A_file_texture_relative_to_the_config_is_reported_as_file()
    {
        using var session = CanvasSession.Start(TexturedStep("shelf_texture = \"floor.png\"\n"), SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        Assert.Equal("none", OpenTextured(session!)["shelf-texture"]);
        Assert.Contains(session!.ErrorLines(), line => line.Contains("shelf_texture", StringComparison.Ordinal) && line.Contains("not found", StringComparison.Ordinal));

        var rgba = new byte[16 * 16 * 4];
        rgba.AsSpan().Fill(200);
        File.WriteAllBytes(Path.Combine(session.RuntimeDir, "floor.png"), PngCodec.Encode(rgba, 16, 16));
        session.Send("reload");
        Assert.DoesNotContain("error:", session.WaitForLine("RELOAD "), StringComparison.Ordinal);
        session.Send("overview close");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=false", 5000));
        Assert.Equal("file", OpenTextured(session)["shelf-texture"]);
    }

    [Fact]
    public void A_file_texture_loads_and_a_missing_one_draws_flat_with_one_warning()
    {
        var runtime = Path.Combine("/tmp", $"basin-texture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runtime);
        try
        {
            var rgba = new byte[32 * 32 * 4];
            for (var i = 0; i < 32 * 32; i++)
            {
                rgba[(i * 4) + 0] = (byte)(i * 7);
                rgba[(i * 4) + 1] = (byte)(i * 3);
                rgba[(i * 4) + 2] = (byte)(i * 5);
                rgba[(i * 4) + 3] = 255;
            }

            var png = Path.Combine(runtime, "floor.png");
            File.WriteAllBytes(png, PngCodec.Encode(rgba, 32, 32));
            File.WriteAllText(Path.Combine(runtime, "junk.png"), "not an image");
            using (var session = CanvasSession.Start(TexturedStep($"shelf_texture = \"{png}\"\nwall_texture = \"{Path.Combine(runtime, "junk.png")}\"\n"), SsdWin()))
            {
                Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
                var overview = OpenTextured(session!);
                Assert.Equal("file", overview["shelf-texture"]);
                Assert.Equal("none", overview["wall-texture"]);
                Assert.Single(session!.ErrorLines(), line => line.Contains("wall_texture", StringComparison.Ordinal) && line.Contains("not an image, drawing flat", StringComparison.Ordinal));
            }

            using (var missing = CanvasSession.Start(TexturedStep("wall_texture = \"stnoe\"\n"), SsdWin()))
            {
                Assert.NotNull(missing);
                var overview = OpenTextured(missing!);
                Assert.Equal("none", overview["wall-texture"]);
                var warning = Assert.Single(missing!.ErrorLines(), line => line.Contains("wall_texture", StringComparison.Ordinal));
                Assert.Contains("stnoe\": not found (presets: stone, brick, wood, noise), drawing flat", warning, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(runtime, recursive: true);
        }
    }

    [Fact]
    public void A_reload_to_a_bad_texture_names_the_error_and_keeps_the_old_one()
    {
        using var session = CanvasSession.Start(TexturedStep("wall_texture = \"stone\"\n"), SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        Assert.Equal("stone", OpenTextured(session!)["wall-texture"]);
        session!.Rewrite(TexturedStep("wall_texture = \"missing/wall.png\"\n"));
        session.Send("reload");
        var reload = session.WaitForLine("RELOAD ");
        Assert.NotNull(reload);
        Assert.Contains(" wall_texture=error:NOT-FOUND", reload, StringComparison.Ordinal);
        session.Send("overview close");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=false", 5000));
        Assert.Equal("stone", OpenTextured(session)["wall-texture"]);

        session.Rewrite(TexturedStep("wall_texture = \"brick\"\nshelf_texture = \"noise\"\n"));
        session.Send("reload");
        var next = session.WaitForLine("RELOAD ");
        Assert.DoesNotContain("error:", next, StringComparison.Ordinal);
        session.Send("overview close");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=false", 5000));
        var overview = OpenTextured(session);
        Assert.Equal("brick", overview["wall-texture"]);
        Assert.Equal("noise", overview["shelf-texture"]);
    }

    [Fact]
    public void A_texture_on_a_slope_wall_warns_once_and_draws_the_slope()
    {
        using var session = CanvasSession.Start(
            "[overview]\ngrid = \"never\"\nanimation_ms = 100\nwall = \"slope\"\nwall_texture = \"stone\"\n", SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var overview = OpenTextured(session!);
        Assert.Equal("slope", overview["wall"]);
        Assert.Equal("none", overview["wall-texture"]);
        Assert.Single(session!.ErrorLines(), line => line.Contains("apply only to wall = \"step\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_step_shelve_animates_from_the_desktop_to_the_shelf_and_back()
    {
        const string slow = "[overview]\ngrid = \"never\"\nanimation_ms = 1000\nwall = \"step\"\n";
        using var session = CanvasSession.Start(slow, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("overview open");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000));
        var (_, _, desk, _, _) = session.CanvasWindow();
        session.Send("shelve right");
        _ = session.WaitForLine("SHELVE ");
        Thread.Sleep(150);
        var (_, _, moving, _, _) = session.CanvasWindow();
        Thread.Sleep(1200);
        var (_, _, shelf, _, _) = session.CanvasWindow();
        Assert.True(moving.X > desk.X + 20 && moving.X < shelf.X - 20, $"mid-shelve at {moving} between {desk} and {shelf}");
        Assert.True(moving.Width < desk.Width && moving.Width > shelf.Width, $"mid-shelve at {moving} between {desk} and {shelf}");

        session.Send("unshelve");
        _ = session.WaitForLine("UNSHELVE ");
        Thread.Sleep(150);
        var (_, _, back, _, _) = session.CanvasWindow();
        Assert.True(back.X < shelf.X - 20 && back.X > desk.X + 20, $"mid-unshelve at {back} between {shelf} and {desk}");
    }
}
