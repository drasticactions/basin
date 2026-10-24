using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Basin.Effects;
using Xunit;
using static TinyComp.Tests.CanvasRows;

namespace TinyComp.Tests;

public sealed class OverviewTests
{
    [Fact]
    public void Overview_zooms_the_desktop_and_shelves_a_window_against_the_screen_edge()
    {
        using var session = CanvasSession.Start(OverviewConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        OpenOverview(session!);
        var desktop = OverviewWindow(session!);
        Assert.Equal("flat", desktop["region"]);
        Assert.Equal("0.75", desktop["k"]);
        Assert.Equal("false", desktop["shelved"]);

        session!.Send("shelve right");
        Assert.Contains("side=right output=HEADLESS-1", session.WaitForLine("SHELVE "));
        _ = session.Shot("shelved");
        var shelved = OverviewWindow(session);
        Assert.Equal("shelf", shelved["region"]);
        Assert.Equal("0.40", shelved["k"]);
        Assert.Equal("true", shelved["shelved"]);
        var (_, _, screen, _, _) = session.CanvasWindow();
        Assert.InRange(screen.Right, 1266, 1281);
    }

    [Fact]
    public void Closing_overview_suspends_a_shelved_window_and_opening_resumes_it()
    {
        using var session = CanvasSession.Start(OverviewConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        OpenOverview(session!);
        session!.Send("shelve right");
        _ = session.WaitForLine("SHELVE ");
        _ = session.Shot("shelved");
        session.Send("overview close");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=false progress=0.00", 5000));
        Assert.NotNull(session.WaitForClientLine("SUSPENDED 1", 5000));
        var closed = session.Shot("closed");
        Assert.Equal(0, Saturated(closed, 0, closed.Width));
        OpenOverview(session);
        Assert.NotNull(session.WaitForClientLine("SUSPENDED 0", 5000));
    }

    [Fact]
    public void A_drag_back_through_both_thresholds_unshelves_on_the_drop()
    {
        using var session = CanvasSession.Start(OverviewConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        OpenOverview(session!);
        session!.Send("shelve right");
        _ = session.WaitForLine("SHELVE ");
        _ = session.Shot("shelved");
        var (_, _, screen, _, _) = session.CanvasWindow();
        var grabY = screen.Y - 5;
        session.Send($"move {screen.X + 20} {grabY}");
        session.Send("button 272 1");
        foreach (var x in new[] { 1170, 1150, 1135, 1128, 1135, 1145, 1128, 1000, 800, 700 })
        {
            session.Send($"move {x} {grabY}");
        }

        session.Send("button 272 0");
        Assert.Contains("held=out", session.WaitForLine("OVERVIEW held="));
        Assert.Contains("held=in", session.WaitForLine("OVERVIEW held="));
        Assert.Contains("held=out", session.WaitForLine("OVERVIEW held="));
        Assert.NotNull(session.WaitForLine("UNSHELVE "));
        _ = session.Shot("unshelved");
        var back = OverviewWindow(session);
        Assert.Equal("false", back["shelved"]);
        Assert.Equal("flat", back["region"]);
    }

    [Fact]
    public void A_click_on_the_zoomed_desktop_reaches_the_client_and_a_click_on_empty_space_closes()
    {
        using var session = CanvasSession.Start(OverviewConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        OpenOverview(session!);
        _ = session!.Shot("open");
        var (_, _, screen, _, _) = session.CanvasWindow();
        Assert.Equal(0.75, session.CanvasScale(), 3);
        var x = screen.X + (screen.Width * 0.5);
        var y = screen.Y + (screen.Height * 0.5);
        session.Send(string.Create(CultureInfo.InvariantCulture, $"move {x} {y}"));
        session.Send("button 272 1");
        session.Send("button 272 0");
        var line = session.WaitForClientLine("BUTTON ");
        Assert.NotNull(line);
        var parts = line!.Split(' ');
        var localX = double.Parse(parts[2], CultureInfo.InvariantCulture);
        var localY = double.Parse(parts[3], CultureInfo.InvariantCulture);
        Assert.True(
            Math.Abs(localX - ((x - screen.X) / 0.75)) < 4.0 && Math.Abs(localY - ((y - screen.Y) / 0.75)) < 4.0,
            $"a click at ({x},{y}) reached ({localX},{localY}) on a window drawn at {screen}");

        session.Send("move 900 600");
        session.Send("button 272 1");
        session.Send("button 272 0");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=false progress=0.00", 5000));
        Assert.Null(session.WaitForClientLine("BUTTON ", 500));
    }

    [Fact]
    public void A_window_mapped_during_overview_zooms_its_frame_with_its_content()
    {
        using var session = CanvasSession.Start(OverviewConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        OpenOverview(session!);
        var before = session!.Shot("before");
        session.Spawn(SsdWin()!, "server");
        Assert.NotNull(session.WaitForLine("MAPPED "));
        var after = session.Shot("after");
        var changed = 0;
        for (var y = 60; y < 110; y++)
        {
            for (var x = 60; x < 200; x++)
            {
                var i = ((y * after.Width) + x) * 4;
                if (after.Rgba[i] != before.Rgba[i] || after.Rgba[i + 1] != before.Rgba[i + 1] || after.Rgba[i + 2] != before.Rgba[i + 2])
                {
                    changed++;
                }
            }
        }

        Assert.Equal(0, changed);
    }

    [Fact]
    public void A_resize_in_overview_keeps_each_window_in_its_own_region()
    {
        using var session = CanvasSession.Start(OverviewConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        OpenOverview(session!);
        _ = session!.Shot("open");
        var (_, _, desk, _, _) = session.CanvasWindow();
        var grabY = desk.Y + (desk.Height / 2);
        session.Send($"move {desk.X - 4} {grabY}");
        session.Send("button 272 1");
        for (var x = desk.X - 20; x > 10; x -= 20)
        {
            session.Send($"move {x} {grabY}");
        }

        session.Send("button 272 0");
        _ = session.Shot("desktop");
        var (deskX, _) = session.WindowPosition();
        Assert.InRange(deskX, 0, 69);
        Assert.Equal("flat", OverviewWindow(session)["region"]);

        session.Send("shelve right");
        _ = session.WaitForLine("SHELVE ");
        _ = session.Shot("shelved");
        var (_, _, shelf, _, _) = session.CanvasWindow();
        var shelfY = shelf.Y + (shelf.Height / 2);
        session.Send($"move {shelf.X - 4} {shelfY}");
        session.Send("button 272 1");
        for (var x = shelf.X; x < shelf.X + 60; x += 10)
        {
            session.Send($"move {x} {shelfY}");
        }

        session.Send("button 272 0");
        _ = session.Shot("shrunk");
        var (_, _, shrunk, _, _) = session.CanvasWindow();
        session.Send($"move {shrunk.X - 4} {shelfY}");
        session.Send("button 272 1");
        for (var x = shrunk.X - 10; x > 1000; x -= 15)
        {
            session.Send($"move {x} {shelfY}");
        }

        var held = session.CanvasFields();
        session.Send("button 272 0");
        _ = session.Shot("released");
        var released = session.CanvasFields();
        Assert.Equal("shelf", released["region"]);
        Assert.InRange(int.Parse(released["screen"].Split(',')[0], CultureInfo.InvariantCulture), 1150, 1200);
        Assert.InRange(
            int.Parse(released["width"], CultureInfo.InvariantCulture),
            int.Parse(held["width"], CultureInfo.InvariantCulture) - 2,
            int.Parse(held["width"], CultureInfo.InvariantCulture) + 2);
        Assert.Equal(double.Parse(held["scale"], CultureInfo.InvariantCulture), double.Parse(released["scale"], CultureInfo.InvariantCulture), 2);
    }

    [Fact]
    public void Overview_opens_with_its_own_sides_and_not_the_canvas_sides()
    {
        using var session = CanvasSession.Start(
            "[canvas]\nsides = [\"top\"]\n[overview]\nsides = [\"left\", \"bottom\"]\nanimation_ms = 100\n", SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("overview open");
        var line = session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000);
        Assert.NotNull(line);
        Assert.Equal("left,bottom", Fields(line!)["sides"]);
    }

    [Theory]
    [InlineData("step")]
    [InlineData("slope")]
    public void A_one_sided_overview_pins_the_far_edge_and_walls_the_empty_axis(string wall)
    {
        var config = $"[overview]\nwall = \"{wall}\"\nsides = [\"left\"]\ngrid = \"never\"\nanimation_ms = 100\n";
        using var session = CanvasSession.Start(config, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        OpenOverview(session!);
        session!.Send("where");
        var line = session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true", 5000);
        Assert.NotNull(line);
        var fields = Fields(line!);
        Assert.Equal("left", fields["sides"]);
        Assert.Equal("fill", fields["anchor"]);
        Assert.Equal("top,bottom", fields["walls"]);
        Assert.Equal(320, int.Parse(fields["shelf"], CultureInfo.InvariantCulture) + int.Parse(fields["slope"], CultureInfo.InvariantCulture));

        var (_, _, before, _, _) = session.CanvasWindow();
        var grabX = before.X + (before.Width / 2);
        var grabY = before.Y + 4;
        session.Send($"move {grabX} {grabY}");
        session.Send("button 272 1");
        for (var y = grabY; y > 8; y -= 20)
        {
            session.Send($"move {grabX} {y}");
        }

        session.Send($"move {grabX} 4");
        session.Send("button 272 0");
        Thread.Sleep(400);
        var dropped = OverviewWindow(session);
        Assert.Equal("false", dropped["shelved"]);
        Assert.Equal("flat", dropped["region"]);
        session.Send("shelve top");
        Assert.Equal("SHELVE refused: side", session.WaitForLine("SHELVE "));

        session.Rewrite(config + "anchor = \"center\"\n");
        session.Send("reload");
        Assert.NotNull(session.WaitForLine("RELOAD "));
        session.Send("where");
        var centered = Fields(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true", 5000)!);
        Assert.Equal("none", centered["walls"]);
        Assert.Equal(160, int.Parse(centered["shelf"], CultureInfo.InvariantCulture) + int.Parse(centered["slope"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Step_overview_zooms_the_desktop_flat_and_shelves_a_window_at_the_shelf_scale()
    {
        using var session = CanvasSession.Start(StepConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("overview open");
        var line = session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000);
        Assert.NotNull(line);
        var overview = Fields(line!);
        Assert.Equal("step", overview["wall"]);
        Assert.Equal("51", overview["slope"]);
        Assert.Equal("109", overview["shelf"]);
        var desktop = OverviewWindow(session);
        Assert.Equal("flat", desktop["region"]);
        Assert.Equal("0.75", desktop["k"]);

        session.Send("shelve right");
        Assert.Contains("side=right output=HEADLESS-1", session.WaitForLine("SHELVE "));
        _ = session.Shot("shelved");
        var shelved = OverviewWindow(session);
        Assert.Equal("shelf", shelved["region"]);
        Assert.Equal("0.40", shelved["k"]);
        var (_, _, screen, _, _) = session.CanvasWindow();
        Assert.InRange(screen.Right, 1278, 1281);
        Assert.True(screen.X >= 1171 - 1, $"the shelved window is drawn at {screen}");
    }

    [Theory]
    [InlineData("[overview]\ngrid = \"never\"\nanimation_ms = 100\nsides = [\"left\", \"right\", \"top\", \"bottom\"]\nwall = \"step\"\n")]
    [InlineData("[overview]\ngrid = \"never\"\nanimation_ms = 100\nsides = [\"left\", \"right\", \"top\", \"bottom\"]\n")]
    public void A_second_shelve_moves_the_shelved_window_to_another_side(string config)
    {
        using var session = CanvasSession.Start(config, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        OpenOverview(session!);
        session!.Send("shelve right");
        Assert.Contains("side=right", session.WaitForLine("SHELVE "));
        session.Send("shelve left");
        Assert.Contains("side=left", session.WaitForLine("SHELVE "));
        _ = session.Shot("left");
        var left = OverviewWindow(session);
        Assert.Equal("left", left["side"]);
        var (_, _, onLeft, _, _) = session.CanvasWindow();
        Assert.InRange(onLeft.X, -1, 3);

        session.Send("shelve top");
        Assert.Contains("side=top", session.WaitForLine("SHELVE "));
        session.Send("shelve top");
        Assert.Equal("SHELVE refused: same side", session.WaitForLine("SHELVE "));
        _ = session.Shot("top");
        Assert.Equal("top", OverviewWindow(session)["side"]);
        var (_, _, onTop, _, _) = session.CanvasWindow();
        Assert.InRange(onTop.Y, -1, 12);

        session.Send("move 640 700");
        session.Send("button 272 1");
        session.Send("button 272 0");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=false", 5000));
        session.Send("shelve right");
        Assert.StartsWith("ERR No window has focus.", session.WaitForLine("ERR "), StringComparison.Ordinal);
    }
}
