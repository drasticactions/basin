using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Basin.Effects;
using Xunit;

namespace Basin.Tests;

public sealed class TinyCompCanvasProcessTests
{
    private const int ZoneWidth = 154;

    private const int ZoneHeight = 86;

    private const string ScaleMode =
        "[canvas]\nenable = true\ngrid = \"never\"\nanimation_ms = 100\nwindow = \"scale\"\n";

    private const string FourSides =
        "[canvas]\nenable = true\ngrid = \"never\"\nanimation_ms = 100\nsides = [\"left\", \"right\", \"top\", \"bottom\"]\n";

    [Fact]
    public void Park_left_draws_the_client_inside_the_zone()
    {
        using var session = CanvasSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var before = session!.Shot("before");
        Assert.True(Saturated(before, ZoneWidth, before.Width - ZoneWidth) > 0, "the client draws in the center before the park");

        session.Send("park left");
        session.WaitForLine("PARK ");
        var parked = session.Shot("parked");
        Assert.True(Saturated(parked, 0, ZoneWidth) > 0, "the client draws inside the left zone after the park");
        Assert.Equal(0, Saturated(parked, ZoneWidth, parked.Width - ZoneWidth));
    }

    [Fact]
    public void A_drag_through_the_seam_parks_the_client_in_the_zone()
    {
        using var session = CanvasSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var (x, y) = session!.WindowPosition();
        session.Send($"move {x + 100} {y + 100}");
        session.Send("key 56 1");
        session.Send("button 272 1");
        for (var cursor = x + 100; cursor > 20; cursor -= 40)
        {
            session.Send($"move {cursor} {y + 100}");
        }

        session.Send("move 10 " + (y + 100));
        session.Send("button 272 0");
        session.Send("key 56 0");
        var dragged = session.Shot("dragged");
        Assert.True(Saturated(dragged, 0, ZoneWidth) > 0, "the client draws inside the left zone after the drag");
        Assert.Equal(0, Saturated(dragged, ZoneWidth, dragged.Width - ZoneWidth));
        var (parkedX, _) = session.WindowPosition();
        Assert.True(parkedX < 0, $"the window's canvas X is {parkedX}");
    }

    [Fact]
    public void Turning_the_canvas_off_recalls_a_parked_window_to_the_centre()
    {
        using var session = CanvasSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var (homeX, _) = session!.WindowPosition();
        session.Send("park left");
        session.WaitForLine("PARK ");
        _ = session.Shot("parked");
        session.Send("canvas off");
        session.WaitForLine("CANVAS off");
        var line = session.WaitForLine("CANVAS view=0 left=0 right=0", timeoutMillis: 5000);
        Assert.NotNull(line);
        var recalled = session.Shot("recalled");
        Assert.Equal(0, Saturated(recalled, 0, ZoneWidth));
        Assert.True(Saturated(recalled, ZoneWidth, recalled.Width - ZoneWidth) > 0, "the client draws in the center again");
        Assert.Equal(homeX, session.WindowPosition().X);
    }

    [Fact]
    public void Park_up_then_left_reaches_the_corner()
    {
        using var session = CanvasSession.Start(FourSides);
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        session!.Send("park up");
        Assert.Contains(" up to=", session.WaitForLine("PARK "));
        session.Send("park left");
        var corner = session.WaitForLine("PARK ");
        Assert.Contains(" left to=", corner);
        Assert.EndsWith(" corner", corner);
        var parked = session.Shot("corner");
        Assert.True(Saturated(parked, 0, 0, ZoneWidth, ZoneHeight) > 0, "the client draws inside the top-left corner");
        Assert.Equal(0, Saturated(parked, ZoneWidth, ZoneHeight, parked.Width - (2 * ZoneWidth), parked.Height - (2 * ZoneHeight)));
        var (canvasX, canvasY, _, _, home) = session.CanvasWindow();
        Assert.True(canvasX < 0 && canvasY < 0, $"the window's canvas position is {canvasX},{canvasY}");
        Assert.NotEqual("none", home);
    }

    [Fact]
    public void A_square_corner_parks_the_window_at_the_far_corner()
    {
        using var session = CanvasSession.Start(FourSides + "corner = \"square\"\n");
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        session!.Send("park up");
        session.WaitForLine("PARK ");
        session.Send("park left");
        var corner = session.WaitForLine("PARK ");
        Assert.Contains($" left to={ZoneWidth - 640} y={ZoneHeight - 360} corner", corner);
        var parked = session.Shot("square");
        Assert.True(Saturated(parked, 0, 0, 12, 12) > 0, "a square corner draws the window into the screen corner");
    }

    [Fact]
    public void A_tapered_corner_parks_the_window_at_the_far_corner()
    {
        using var session = CanvasSession.Start(FourSides + "corner = \"taper\"\n");
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        session!.Send("park up");
        session.WaitForLine("PARK ");
        session.Send("park left");
        var corner = session.WaitForLine("PARK ");
        Assert.Contains($" left to={ZoneWidth - 640} y={ZoneHeight - 360} corner", corner);
        var parked = session.Shot("taper");
        Assert.True(Saturated(parked, 0, 0, 12, 12) > 0, "a tapered corner draws the window into the screen corner");
    }

    [Fact]
    public void Recall_returns_both_axes_home()
    {
        using var session = CanvasSession.Start(FourSides);
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var home = session!.WindowPosition();
        session.Send("park up");
        session.WaitForLine("PARK ");
        session.Send("park left");
        session.WaitForLine("PARK ");
        _ = session.Shot("parked");
        Assert.NotEqual(home, session.WindowPosition());
        session.Send("recall");
        Assert.Contains($"to={home.X} y={home.Y}", session.WaitForLine("RECALL "));
        _ = session.Shot("recalled");
        Assert.Equal(home, session.WindowPosition());
    }

    [Fact]
    public void Park_up_is_refused_without_a_top_zone()
    {
        using var session = CanvasSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var home = session!.WindowPosition();
        session.Send("park up");
        Assert.Contains("refused: no top zone", session.WaitForLine("PARK "));
        Assert.Equal(home, session.WindowPosition());
    }

    [Fact]
    public void A_bottom_right_rule_parks_on_both_axes()
    {
        using var session = CanvasSession.Start(
            FourSides + "[[rule]]\napp_id = \"org.freedesktop.weston.simple-shm\"\ncanvas = \"bottom-right\"\n");
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        _ = session!.Shot("ruled");
        var (x, y) = session.WindowPosition();
        Assert.True(x + 250 > 1280 - ZoneWidth && y + 250 > 720 - ZoneHeight, $"the window's canvas position is {x},{y}");
        Assert.True(x > 1280 - ZoneWidth - 250 + 100 && y > 720 - ZoneHeight - 250 + 100, $"the window's canvas position is {x},{y}");
    }

    [Fact]
    public void A_click_in_the_corner_reaches_the_client_at_true_surface_coordinates()
    {
        using var session = CanvasSession.Start(FourSides, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("park up");
        session.WaitForLine("PARK ");
        session.Send("park left");
        session.WaitForLine("PARK ");
        _ = session.Shot("corner");
        var (canvasX, canvasY, screen, _, _) = session.CanvasWindow();

        var map = LayoutMap();
        var clicks = new[] { (0.5, 0.5), (0.25, 0.75), (0.8, 0.3) };
        foreach (var (fx, fy) in clicks)
        {
            var x = Math.Round(screen.X + (screen.Width * fx), 2);
            var y = Math.Round(screen.Y + (screen.Height * fy), 2);
            var (expectedX, expectedY) = map.ToCanvasPoint(x, y);
            session.Send(string.Create(CultureInfo.InvariantCulture, $"move {x} {y}"));
            session.Send("button 272 1");
            session.Send("button 272 0");
            var line = session.WaitForClientLine("BUTTON ");
            Assert.NotNull(line);
            var parts = line!.Split(' ');
            var localX = double.Parse(parts[2], CultureInfo.InvariantCulture);
            var localY = double.Parse(parts[3], CultureInfo.InvariantCulture);
            Assert.True(
                Math.Abs(localX - (expectedX - canvasX)) < 1.0 && Math.Abs(localY - (expectedY - canvasY)) < 1.0,
                $"a click at ({x},{y}) reached ({localX},{localY}), the canvas says ({expectedX - canvasX:F2},{expectedY - canvasY:F2})");
        }
    }

    [Fact]
    public void Scale_mode_parks_a_flat_window_at_the_floor_inside_the_output()
    {
        using var session = CanvasSession.Start(ScaleMode);
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        session!.Send("park right");
        var park = session.WaitForLine("PARK ");
        Assert.Contains(" scale=0.350", park);
        var parked = session.Shot("scaled");
        Assert.True(Saturated(parked, parked.Width - ZoneWidth, ZoneWidth) > 0, "the client draws inside the right zone");
        Assert.Equal(0, Saturated(parked, 0, parked.Width - ZoneWidth));
        var (_, _, screen, deformed, home) = session.CanvasWindow();
        Assert.True(deformed);
        Assert.NotEqual("none", home);
        Assert.True(screen.Right <= 1280 && screen.Right >= 1278, $"the drawn box is {screen}");
        Assert.Equal(0.350, session.CanvasScale(), 2);

        session.Send("recall");
        session.WaitForLine("RECALL ");
        Thread.Sleep(400);
        Assert.Equal(1.0, session.CanvasScale(), 3);
    }

    [Fact]
    public void A_click_on_a_scaled_window_reaches_the_client_at_true_surface_coordinates()
    {
        using var session = CanvasSession.Start(ScaleMode, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("park right");
        session.WaitForLine("PARK ");
        _ = session.Shot("scaled");
        var (_, _, screen, _, _) = session.CanvasWindow();
        var scale = session.CanvasScale();
        foreach (var (fx, fy) in new[] { (0.5, 0.5), (0.2, 0.8), (0.9, 0.3) })
        {
            var x = Math.Round(screen.X + (screen.Width * fx), 2);
            var y = Math.Round(screen.Y + (screen.Height * fy), 2);
            session.Send(string.Create(CultureInfo.InvariantCulture, $"move {x} {y}"));
            session.Send("button 272 1");
            session.Send("button 272 0");
            var line = session.WaitForClientLine("BUTTON ");
            Assert.NotNull(line);
            var parts = line!.Split(' ');
            var localX = double.Parse(parts[2], CultureInfo.InvariantCulture);
            var localY = double.Parse(parts[3], CultureInfo.InvariantCulture);
            Assert.True(
                Math.Abs(localX - ((x - screen.X) / scale)) < 4.0 && Math.Abs(localY - ((y - screen.Y) / scale)) < 4.0,
                $"a click at ({x},{y}) reached ({localX},{localY}) on a window drawn at {screen} and {scale}");
        }
    }

    [Fact]
    public void Resizing_the_inner_edge_of_a_scaled_window_keeps_the_outer_edge_still()
    {
        using var session = CanvasSession.Start(ScaleMode, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("park right");
        session.WaitForLine("PARK ");
        _ = session.Shot("parked");
        var (_, _, before, _, _) = session.CanvasWindow();
        var y = before.Y + (before.Height / 2);
        session.Send($"move {before.X - 3} {y}");
        session.Send("button 272 1");
        foreach (var x in new[] { before.X - 20, before.X - 40, before.X - 63 })
        {
            session.Send($"move {x} {y}");
            Thread.Sleep(300);
            var (_, _, during, _, _) = session.CanvasWindow();
            Assert.True(Math.Abs(during.Right - before.Right) <= 1, $"the outer edge moved from {before} to {during}");
            Assert.True(Math.Abs(during.X - (x + 3)) <= 2, $"the inner edge is at {during.X} for a cursor at {x}");
        }

        session.Send("button 272 0");
    }

    [Fact]
    public void A_client_that_resizes_for_a_smaller_scale_loses_the_offer_and_keeps_its_size()
    {
        using var session = CanvasSession.Start(ScaleMode, SsdWin(), ["server", "pixel"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        Assert.Equal("SIZE 256 256", session!.WaitForClientLine("SIZE "));
        session.Send("park right");
        session.WaitForLine("PARK ");
        Assert.NotNull(session.WaitForClientLine("SIZE 731"));
        Assert.NotNull(session.WaitForClientLine("SIZE 256"));
        Thread.Sleep(600);
        var (_, _, screen, _, _) = session.CanvasWindow();
        Assert.True(screen.Width < 100, $"the window still draws scaled: {screen}");
        Assert.Equal(0.35, session.CanvasScale(), 1);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void A_window_dragged_into_the_well_reaches_its_smallest_size_at_the_screen_edge_wherever_it_was_grabbed(double grab)
    {
        using var session = CanvasSession.Start(ScaleMode, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var (_, _, start, _, _) = session!.CanvasWindow();
        var grabX = start.X + (int)(grab * start.Width);
        var y = start.Y + 80;
        session.Send($"move {grabX} {y}");
        session.Send("key 56 1");
        session.Send("button 272 1");
        var previous = 1.0;
        var floored = false;
        for (var x = grabX + 20; x < 1280 + 20; x += 20)
        {
            var cursor = Math.Min(x, 1279);
            session.Send($"move {cursor} {y}");
            var (_, _, drawn, _, _) = session.CanvasWindow();
            var scale = session.CanvasScale();
            Assert.True(Math.Abs(((cursor - drawn.X) / (double)drawn.Width) - grab) < 0.03, $"the grabbed point left the cursor at {cursor}: {drawn}");
            Assert.True(scale <= previous + 0.005 && previous - scale < 0.2, $"the scale jumped from {previous} to {scale} at {cursor}");
            if (scale > 0.36)
            {
                Assert.True(drawn.Right <= 1281, $"the window left the screen before its smallest size at {cursor}: {drawn}");
            }
            else if (!floored)
            {
                floored = true;
                Assert.True(drawn.Right >= 1280 - 24 && drawn.Right <= 1280 + 24, $"the smallest size arrived away from the edge at {cursor}: {drawn}");
            }

            previous = scale;
        }

        var (_, _, before, _, _) = session.CanvasWindow();
        session.Send("button 272 0");
        session.Send("key 56 0");
        Thread.Sleep(500);
        var (_, _, after, _, _) = session.CanvasWindow();
        Assert.True(floored, "the window reached its smallest size");
        Assert.Equal(0.35, session.CanvasScale(), 2);
        Assert.True(Math.Abs(after.X - before.X) <= 2 && Math.Abs(after.Y - before.Y) <= 2, $"the drop moved the window from {before} to {after}");
    }

    [Fact]
    public void A_reload_into_scale_mode_moves_a_parked_window_to_the_new_depth()
    {
        using var session = CanvasSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        session!.Send("park right");
        var warp = session.WaitForLine("PARK ");
        Assert.DoesNotContain("scale=", warp);
        Thread.Sleep(400);
        var (warpX, _) = session.WindowPosition();
        session.Rewrite(ScaleMode);
        session.Send("reload");
        var reload = session.WaitForLine("RELOAD ");
        Assert.Contains("canvas-window=scale", reload);
        var (scaleX, _) = session.WindowPosition();
        Assert.True(scaleX < warpX, $"the park depth moved from {warpX} to {scaleX}");
        var (_, _, screen, _, _) = session.CanvasWindow();
        Assert.True(screen.Right <= 1280, $"the drawn box is {screen}");
    }

    [Fact]
    public void A_side_dock_moves_the_seam_inside_it_and_stays_flat()
    {
        using var session = CanvasSession.Start(FourSides);
        Assert.SkipWhen(session is null || Panel() is null, "tinycomp, weston-simple-shm or panel is not available beside the tests");
        session!.Spawn(Panel()!, "left", "60");
        session.Spawn(Panel()!, "top", "30");
        Thread.Sleep(600);
        session.Send("park left");
        Assert.Contains("to=" + (60 + ZoneWidth - 640), session.WaitForLine("PARK "));
        session.Send("park up");
        var corner = session.WaitForLine("PARK ");
        var rimY = (int)Math.Round(30 + ZoneHeight - (360 / Math.Sqrt(2)));
        Assert.Contains($" y={rimY} corner", corner);
        var parked = session.Shot("docked");
        Assert.Equal(0, Saturated(parked, 0, 0, 60, parked.Height));
        Assert.Equal(0, Saturated(parked, 0, 0, parked.Width, 30));
        Assert.True(Saturated(parked, 60, 30, ZoneWidth, ZoneHeight) > 0, "the client draws in the corner inside both panels");
    }

    [Fact]
    public void A_dock_that_maps_late_keeps_a_parked_window_out_from_under_it()
    {
        using var session = CanvasSession.Start(FourSides);
        Assert.SkipWhen(session is null || Panel() is null, "tinycomp, weston-simple-shm or panel is not available beside the tests");
        session!.Send("park left");
        session.WaitForLine("PARK ");
        var before = session.Shot("before");
        Assert.True(Saturated(before, 0, 60) > 0, "the parked client reaches the screen edge before the dock");
        session.Spawn(Panel()!, "left", "60");
        Assert.NotNull(session.WaitForLine("CANVAS view=0"));
        var after = session.Shot("after");
        Assert.Equal(60 + ZoneWidth - 640, session.WindowPosition().X);
        Assert.Equal(0, Saturated(after, 0, 60));
        Assert.True(Saturated(after, 60, ZoneWidth) > 0, "the parked client draws beside the dock");
    }

    private static string? Panel([CallerFilePath] string sourcePath = "") => WlClient("panel", sourcePath);

    private static CanvasWarpTransform LayoutMap()
    {
        var left = new CanvasWarp();
        left.Layout(ZoneWidth, -1, ZoneWidth, 640, 0.2, 0.25, 360);
        var right = new CanvasWarp(1);
        right.Layout(1280 - ZoneWidth, 1, ZoneWidth, 640, 0.2, 0.25, 360);
        var top = new CanvasWarp();
        top.Layout(ZoneHeight, -1, ZoneHeight, 360, 0.2, 0.25, 640);
        var bottom = new CanvasWarp(1);
        bottom.Layout(720 - ZoneHeight, 1, ZoneHeight, 360, 0.2, 0.25, 640);
        return new CanvasWarpTransform
        {
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
        };
    }

    [Fact]
    public void Terrace_mode_parks_on_the_shelf_and_a_shelf_change_keeps_the_outer_edge()
    {
        using var session = CanvasSession.Start(clientPath: SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("canvas mode terrace");
        Assert.Equal("CANVASMODE view=0 window=terrace", session.WaitForLine("CANVASMODE "));
        session.Send("park right");
        Assert.Contains(" scale=0.400", session.WaitForLine("PARK "));
        Thread.Sleep(400);
        var parked = session.CanvasFields();
        Assert.Equal("shelf", parked["region"]);
        Assert.Equal(0.4, double.Parse(parked["k"], CultureInfo.InvariantCulture), 2);
        var (_, _, before, _, home) = session.CanvasWindow();
        Assert.NotEqual("none", home);
        Assert.InRange(before.Right, 1276, 1280);

        session.Send("shelf smaller");
        Assert.Equal("SHELF view=0 side=right scale=0.35 from=0.40", session.WaitForLine("SHELF "));
        Thread.Sleep(400);
        var smaller = session.CanvasFields();
        Assert.Equal("shelf", smaller["region"]);
        Assert.Equal(0.35, double.Parse(smaller["k"], CultureInfo.InvariantCulture), 2);
        var (_, _, after, _, _) = session.CanvasWindow();
        Assert.True(Math.Abs(after.Right - before.Right) <= 2, $"the outer edge moved from {before} to {after}");
        Assert.True(Math.Abs((after.Y + (after.Height / 2)) - (before.Y + (before.Height / 2))) <= 2, $"the center moved from {before} to {after}");
        Assert.True(after.Width < before.Width, $"the window did not shrink: {before} to {after}");

        session.Send("shelf");
        Assert.Equal("SHELF view=0 window=terrace scales=0.40,0.35,0.40,0.40", session.WaitForLine("SHELF "));
        session.Send("shelf reset");
        Assert.Equal("SHELF view=0 side=right scale=0.40 from=0.35", session.WaitForLine("SHELF "));
        session.Send("recall");
        session.WaitForLine("RECALL ");
        Thread.Sleep(400);
        var recalled = session.CanvasFields();
        Assert.Equal(home, recalled["canvas"]);
        Assert.Equal("1.000", recalled["k"]);
    }

    [Fact]
    public void A_shelf_step_on_a_warp_output_is_refused()
    {
        using var session = CanvasSession.Start();
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        session!.Send("shelf larger");
        Assert.Equal("SHELF view=0 refused: window=warp", session.WaitForLine("SHELF "));
        session.Send("canvas mode");
        Assert.Equal("CANVASMODE view=0 window=scale", session.WaitForLine("CANVASMODE "));
        session.Send("canvas mode");
        Assert.Equal("CANVASMODE view=0 window=terrace", session.WaitForLine("CANVASMODE "));
        session.Send("canvas mode");
        Assert.Equal("CANVASMODE view=0 window=warp", session.WaitForLine("CANVASMODE "));
    }

    [Fact]
    public void A_click_on_a_shelf_window_reaches_the_client_at_true_surface_coordinates()
    {
        using var session = CanvasSession.Start(
            "[canvas]\nenable = true\ngrid = \"never\"\nanimation_ms = 100\nwindow = \"terrace\"\n", SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("park right");
        session.WaitForLine("PARK ");
        _ = session.Shot("shelf");
        var (_, _, screen, _, _) = session.CanvasWindow();
        var scale = session.CanvasScale();
        Assert.InRange(scale, 0.3, 0.5);
        foreach (var (fx, fy) in new[] { (0.5, 0.5), (0.2, 0.8), (0.9, 0.3) })
        {
            var x = Math.Round(screen.X + (screen.Width * fx), 2);
            var y = Math.Round(screen.Y + (screen.Height * fy), 2);
            session.Send(string.Create(CultureInfo.InvariantCulture, $"move {x} {y}"));
            session.Send("button 272 1");
            session.Send("button 272 0");
            var line = session.WaitForClientLine("BUTTON ");
            Assert.NotNull(line);
            var parts = line!.Split(' ');
            var localX = double.Parse(parts[2], CultureInfo.InvariantCulture);
            var localY = double.Parse(parts[3], CultureInfo.InvariantCulture);
            Assert.True(
                Math.Abs(localX - ((x - screen.X) / scale)) < 4.0 && Math.Abs(localY - ((y - screen.Y) / scale)) < 4.0,
                $"a click at ({x},{y}) reached ({localX},{localY}) on a window drawn at {screen} and {scale}");
        }
    }

    private const string OverviewConfig = "[overview]\ngrid = \"never\"\nanimation_ms = 100\n";

    private static Dictionary<string, string> Fields(string line)
    {
        var fields = new Dictionary<string, string>();
        foreach (var part in line.Split(' '))
        {
            var equals = part.IndexOf('=');
            if (equals > 0)
            {
                fields[part[..equals]] = part[(equals + 1)..];
            }
        }

        return fields;
    }

    private static Dictionary<string, string> OverviewWindow(CanvasSession session)
    {
        session.Send("where");
        var line = session.WaitForLine("OVERVIEWWIN ");
        Assert.NotNull(line);
        return Fields(line!);
    }

    private static void OpenOverview(CanvasSession session)
    {
        session.Send("overview open");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000));
    }

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

    private const string StepConfig = "[overview]\ngrid = \"never\"\nanimation_ms = 100\nwall = \"step\"\n";

    private const string StepFourConfig =
        "[overview]\ngrid = \"never\"\nanimation_ms = 100\nsides = [\"left\", \"right\", \"top\", \"bottom\"]\nwall = \"step\"\n";

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
        Assert.Equal("ERR no focused window", session.WaitForLine("ERR "));
    }

    private static string TexturedStep(string keys) =>
        "[overview]\ngrid = \"never\"\nanimation_ms = 100\nwall = \"step\"\n" + keys;

    private static Dictionary<string, string> OpenTextured(CanvasSession session)
    {
        session.Send("overview open");
        var line = session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000);
        Assert.NotNull(line);
        return Fields(line!);
    }

    private static double WallSpread(Shot shot)
    {
        var sum = 0.0;
        var squares = 0.0;
        var count = 0;
        for (var y = 200; y < 520; y++)
        {
            for (var x = 1124; x < 1150; x++)
            {
                var i = ((y * shot.Width) + x) * 4;
                var value = shot.Rgba[i] + shot.Rgba[i + 1] + shot.Rgba[i + 2];
                sum += value;
                squares += (double)value * value;
                count++;
            }
        }

        var mean = sum / count;
        return Math.Sqrt(Math.Max(0, (squares / count) - (mean * mean)));
    }

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

    private static int BlueGridPixels(Shot shot)
    {
        var count = 0;
        for (var i = 0; i < shot.Rgba.Length; i += 4)
        {
            if (shot.Rgba[i + 2] > 150 && shot.Rgba[i] < 90 && shot.Rgba[i + 1] < 110)
            {
                count++;
            }
        }

        return count;
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

    private const string BareTerrace = "[canvas]\nenable = true\nwindow = \"terrace\"\ngrid = \"always\"\nanimation_ms = 100\n";

    private static int GridPixelsIn(Shot shot, int x0, int y0, int x1, int y1, Box skip)
    {
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (x >= skip.X - 40 && x < skip.Right + 40 && y >= skip.Y - 40 && y < skip.Bottom + 40)
                {
                    continue;
                }

                var i = ((y * shot.Width) + x) * 4;
                if (shot.Rgba[i + 2] > 150 && shot.Rgba[i] < 90 && shot.Rgba[i + 1] < 110)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void AssertBarePlateau(CanvasSession session, string config, int x0, int y0, int x1, int y1, Action open)
    {
        open();
        var (_, _, window, _, _) = session.CanvasWindow();
        var bare = session.Shot("bare");
        var plateau = GridPixelsIn(bare, x0, y0, x1, y1, window);
        var shelf = GridPixelsIn(bare, 0, 0, 100, bare.Height, window);
        Assert.True(plateau < 200, $"{plateau} grid pixels are left on the desktop");
        Assert.True(shelf > 1000, $"the shelf drew {shelf} grid pixels");

        session.Rewrite(config.Replace("grid = \"always\"\n", "grid = \"always\"\ndesktop_grid = true\n", StringComparison.Ordinal));
        session.Send("reload");
        Assert.NotNull(session.WaitForLine("RELOAD "));
        open();
        var lined = GridPixelsIn(session.Shot("lined"), x0, y0, x1, y1, window);
        Assert.True(lined > 3000, $"desktop_grid = true drew {lined} grid pixels on the desktop");
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

    private static (int First, int Last) RedSpan(Shot shot, int y)
    {
        var first = -1;
        var last = -1;
        for (var x = 0; x < shot.Width; x++)
        {
            var i = ((y * shot.Width) + x) * 4;
            if (shot.Rgba[i] > 150 && shot.Rgba[i + 1] < 80 && shot.Rgba[i + 2] < 80)
            {
                first = first < 0 ? x : first;
                last = x;
            }
        }

        return (first, last);
    }

    [Fact]
    public void A_wallpaper_fills_the_desktop_between_the_seams_until_it_is_asked_for_the_output()
    {
        const string config = "[canvas]\nenable = true\nwindow = \"terrace\"\ngrid = \"never\"\nanimation_ms = 100\n";
        using var session = CanvasSession.Start(config, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        Assert.SkipWhen(!File.Exists("/usr/bin/swaybg"), "swaybg is not installed");
        session!.Spawn("/usr/bin/swaybg", "-c", "#c02020");
        Thread.Sleep(1500);
        var (first, last) = RedSpan(session.Shot("desktop"), 700);
        Assert.InRange(first, 228, 232);
        Assert.InRange(last, 1047, 1051);

        session.Rewrite(config + "wallpaper = \"output\"\n");
        session.Send("reload");
        Assert.NotNull(session.WaitForLine("RELOAD "));
        Thread.Sleep(800);
        Assert.Equal((0, 1279), RedSpan(session.Shot("output"), 700));
    }

    [Fact]
    public void A_file_texture_relative_to_the_config_is_reported_as_file()
    {
        using var session = CanvasSession.Start(TexturedStep("shelf_texture = \"floor.png\"\n"), SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        Assert.Equal("none", OpenTextured(session!)["shelf-texture"]);
        Assert.Contains(session!.ErrorLines(), line => line.Contains("shelf_texture", StringComparison.Ordinal) && line.Contains("NOT FOUND", StringComparison.Ordinal));

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
        var runtime = Path.Combine("/tmp", $"basin-texture-{Environment.ProcessId}-{Environment.TickCount64 % 100000}");
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
                Assert.Single(session!.ErrorLines(), line => line.Contains("wall_texture", StringComparison.Ordinal) && line.Contains("NOT AN IMAGE, drawing flat", StringComparison.Ordinal));
            }

            using (var missing = CanvasSession.Start(TexturedStep("wall_texture = \"stnoe\"\n"), SsdWin()))
            {
                Assert.NotNull(missing);
                var overview = OpenTextured(missing!);
                Assert.Equal("none", overview["wall-texture"]);
                var warning = Assert.Single(missing!.ErrorLines(), line => line.Contains("wall_texture", StringComparison.Ordinal));
                Assert.Contains("stnoe\": NOT FOUND (presets: stone, brick, wood, noise), drawing flat", warning, StringComparison.Ordinal);
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
            "[overview]\ngrid = \"never\"\nanimation_ms = 100\nwall_texture = \"stone\"\n", SsdWin());
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

    [Fact]
    public void A_step_drag_across_the_wall_changes_scale_smoothly_and_unshelves_on_the_drop()
    {
        using var session = CanvasSession.Start(StepConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("overview open");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000));
        session.Send("shelve right");
        _ = session.WaitForLine("SHELVE ");
        _ = session.Shot("shelved");
        var (_, _, screen, _, _) = session.CanvasWindow();
        var grabX = screen.X + 20;
        var grabY = screen.Y - 5;
        session.Send($"move {grabX} {grabY}");
        session.Send("button 272 1");
        var scales = new List<double>();
        foreach (var x in new[] { 1190, 1175, 1165, 1155, 1145, 1135, 1125, 1115, 1000, 900 })
        {
            session.Send($"move {x} {grabY}");
            scales.Add(session.CanvasScale());
        }

        for (var i = 1; i < scales.Count; i++)
        {
            Assert.True(scales[i] >= scales[i - 1] - 0.001, $"the scale fell from {scales[i - 1]} to {scales[i]}");
            Assert.True(scales[i] - scales[i - 1] < 0.12, $"the scale jumped from {scales[i - 1]} to {scales[i]}");
        }

        Assert.Equal(0.75, scales[^1], 2);
        session.Send("button 272 0");
        Assert.NotNull(session.WaitForLine("UNSHELVE "));
        _ = session.Shot("unshelved");
        var back = OverviewWindow(session);
        Assert.Equal("false", back["shelved"]);
        Assert.Equal("flat", back["region"]);
        Assert.Equal("0.75", back["k"]);
    }

    [Fact]
    public void A_step_drag_into_a_corner_rests_there_as_one_uniform_scale()
    {
        using var session = CanvasSession.Start(StepFourConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("overview open");
        var line = session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000);
        Assert.Contains("sides=left,right,top,bottom", line);
        _ = session.Shot("open");
        var (_, _, screen, _, _) = session.CanvasWindow();
        var grabX = screen.X + 20;
        var grabY = screen.Y - 5;
        session.Send($"move {grabX} {grabY}");
        session.Send("button 272 1");
        for (var step = 1; step <= 10; step++)
        {
            session.Send($"move {grabX + ((1265 - grabX) * step / 10)} {grabY + ((20 - grabY) * step / 10)}");
        }

        session.Send("button 272 0");
        Assert.Contains("held=in", session.WaitForLine("OVERVIEW held="));
        Assert.Contains("side=right-top", session.WaitForLine("SHELVE "));
        _ = session.Shot("corner");
        var corner = OverviewWindow(session);
        Assert.Equal("corner", corner["region"]);
        var (_, _, drawn, _, _) = session.CanvasWindow();
        Assert.True(drawn.X >= 1171 - 1 && drawn.Right <= 1281, $"the corner window is drawn at {drawn}");
        Assert.True(drawn.Y >= -1 && drawn.Bottom <= 62, $"the corner window is drawn at {drawn}");
    }

    [Fact]
    public void A_click_on_a_step_wall_closes_overview()
    {
        using var session = CanvasSession.Start(StepConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("overview open");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000));
        _ = session.Shot("open");
        session.Send("move 1145 600");
        session.Send("button 272 1");
        session.Send("button 272 0");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=false progress=0.00", 5000));
        Assert.Null(session.WaitForClientLine("BUTTON ", 500));
    }

    [Fact]
    public void A_step_shelf_resize_toward_the_desktop_stops_at_the_base_line()
    {
        using var session = CanvasSession.Start(StepConfig, SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("overview open");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000));
        session.Send("shelve right");
        _ = session.WaitForLine("SHELVE ");
        _ = session.Shot("shelved");
        var (_, _, shelf, _, _) = session.CanvasWindow();
        var shelfY = shelf.Y + (shelf.Height / 2);
        session.Send($"move {shelf.X - 4} {shelfY}");
        session.Send("button 272 1");
        for (var x = shelf.X - 10; x > 1000; x -= 15)
        {
            session.Send($"move {x} {shelfY}");
        }

        var held = session.CanvasFields();
        session.Send("button 272 0");
        _ = session.Shot("released");
        var released = session.CanvasFields();
        Assert.Equal("shelf", released["region"]);
        Assert.InRange(int.Parse(released["screen"].Split(',')[0], CultureInfo.InvariantCulture), 1170, 1200);
        Assert.InRange(
            int.Parse(released["width"], CultureInfo.InvariantCulture),
            int.Parse(held["width"], CultureInfo.InvariantCulture) - 2,
            int.Parse(held["width"], CultureInfo.InvariantCulture) + 2);
    }

    [Fact]
    public void Overview_is_refused_on_an_output_with_the_canvas_enabled()
    {
        using var session = CanvasSession.Start("[canvas]\nenable = true\ngrid = \"never\"\n", SsdWin());
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        session!.Send("overview open");
        Assert.Equal("OVERVIEW refused: canvas", session.WaitForLine("OVERVIEW "));
        session.Send("shelve right");
        Assert.Equal("SHELVE refused: canvas", session.WaitForLine("SHELVE "));
    }

    private static string? SsdWin([CallerFilePath] string sourcePath = "") => WlClient("ssdwin", sourcePath);

    private static string? WlClient(string name, string sourcePath)
    {
        var tests = Path.GetDirectoryName(sourcePath);
        var root = tests is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(tests));
        var candidate = root is null ? null : Path.Combine(root, "scripts", "wlclients", "bin", name);
        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }

    private static int Saturated(Shot shot, int fromX, int width) => Saturated(shot, fromX, 0, width, shot.Height);

    private static int Saturated(Shot shot, int fromX, int fromY, int width, int height)
    {
        var count = 0;
        for (var y = fromY; y < fromY + height; y++)
        {
            for (var x = fromX; x < fromX + width; x++)
            {
                var i = ((y * shot.Width) + x) * 4;
                var r = shot.Rgba[i];
                var g = shot.Rgba[i + 1];
                var b = shot.Rgba[i + 2];
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                if (max - min > 100 && (r > 60 || g > 60))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private readonly record struct Shot(byte[] Rgba, int Width, int Height);

    private sealed class CanvasSession : IDisposable
    {
        private readonly Process _compositor;
        private readonly string _runtimeDir;
        private readonly List<string> _lines = [];
        private readonly List<string> _clientLines = [];
        private readonly List<string> _errorLines = [];
        private readonly List<Process> _extra = [];
        private string _socket = string.Empty;
        private string? _options;
        private readonly object _gate = new();
        private Process? _client;

        private CanvasSession(Process compositor, string runtimeDir)
        {
            _compositor = compositor;
            _runtimeDir = runtimeDir;
        }

        public static CanvasSession? Start(
            string config = "[canvas]\nenable = true\ngrid = \"never\"\nanimation_ms = 100\n",
            string? clientPath = "weston-simple-shm",
            string[]? clientArguments = null,
            string[]? compositorArguments = null)
        {
            if (!OperatingSystem.IsLinux() || Locate("tinycomp") is not { } compositorPath || clientPath is null ||
                (clientPath == "weston-simple-shm" && !ClientAvailable()))
            {
                return null;
            }

            var tag = $"{Environment.ProcessId}-{Environment.TickCount64 % 100000}";
            var runtimeDir = Path.Combine("/tmp", $"basin-canvas-{tag}");
            Directory.CreateDirectory(runtimeDir);
            File.SetUnixFileMode(runtimeDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var configPath = Path.Combine(runtimeDir, "tinycomp.toml");
            File.WriteAllText(configPath, config);

            var info = new ProcessStartInfo(compositorPath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("--backend");
            info.ArgumentList.Add("headless");
            info.ArgumentList.Add("--renderer");
            info.ArgumentList.Add("pixman");
            info.ArgumentList.Add("--config");
            info.ArgumentList.Add(configPath);
            foreach (var argument in compositorArguments ?? [])
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
            info.Environment.Remove("WAYLAND_DISPLAY");
            var compositor = Process.Start(info)!;
            var session = new CanvasSession(compositor, runtimeDir);
            compositor.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    lock (session._gate)
                    {
                        session._lines.Add(line);
                        if (line.StartsWith("OPTIONS ", StringComparison.Ordinal))
                        {
                            session._options = line;
                        }

                        Monitor.PulseAll(session._gate);
                    }
                }
            };
            compositor.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    lock (session._gate)
                    {
                        session._errorLines.Add(line);
                    }
                }
            };
            compositor.BeginOutputReadLine();
            compositor.BeginErrorReadLine();

            var socketLine = session.WaitForLine("SOCKET ");
            if (socketLine is null)
            {
                session.Dispose();
                return null;
            }

            var clientInfo = new ProcessStartInfo(clientPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in clientArguments ?? [])
            {
                clientInfo.ArgumentList.Add(argument);
            }

            clientInfo.Environment["XDG_RUNTIME_DIR"] = runtimeDir;
            session._socket = socketLine.Split(' ')[1];
            clientInfo.Environment["WAYLAND_DISPLAY"] = session._socket;
            var client = Process.Start(clientInfo)!;
            client.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    lock (session._gate)
                    {
                        session._clientLines.Add(line);
                        Monitor.PulseAll(session._gate);
                    }
                }
            };
            client.ErrorDataReceived += (_, _) => { };
            client.BeginOutputReadLine();
            client.BeginErrorReadLine();
            session._client = client;

            if (session.WaitForLine("MAPPED ") is null)
            {
                session.Dispose();
                return null;
            }

            Thread.Sleep(600);
            return session;
        }

        public void Spawn(string path, params string[] arguments)
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["XDG_RUNTIME_DIR"] = _runtimeDir;
            info.Environment["WAYLAND_DISPLAY"] = _socket;
            var process = Process.Start(info)!;
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _extra.Add(process);
        }

        public void Send(string command)
        {
            _compositor.StandardInput.WriteLine(command);
            _compositor.StandardInput.Flush();
            Thread.Sleep(60);
        }

        public string? WaitForLine(string prefix, int timeoutMillis = 15000) => WaitIn(_lines, prefix, timeoutMillis);

        public string? WaitForClientLine(string prefix, int timeoutMillis = 15000) => WaitIn(_clientLines, prefix, timeoutMillis);

        private string? WaitIn(List<string> lines, string prefix, int timeoutMillis)
        {
            var deadline = Environment.TickCount64 + timeoutMillis;
            lock (_gate)
            {
                var from = 0;
                while (true)
                {
                    for (; from < lines.Count; from++)
                    {
                        if (lines[from].StartsWith(prefix, StringComparison.Ordinal))
                        {
                            var found = lines[from];
                            lines.RemoveRange(0, from + 1);
                            return found;
                        }
                    }

                    var remaining = deadline - Environment.TickCount64;
                    if (remaining <= 0 || _compositor.HasExited)
                    {
                        return null;
                    }

                    Monitor.Wait(_gate, (int)Math.Min(remaining, 500));
                }
            }
        }

        public (int X, int Y) WindowPosition()
        {
            Send("where");
            var line = WaitForLine("WIN ");
            Assert.NotNull(line);
            var parts = line!.Split(' ');
            return (int.Parse(parts[2]), int.Parse(parts[3]));
        }

        public void Rewrite(string config) => File.WriteAllText(Path.Combine(_runtimeDir, "tinycomp.toml"), config);

        public string RuntimeDir => _runtimeDir;

        public string[] ErrorLines()
        {
            lock (_gate)
            {
                return [.. _errorLines];
            }
        }

        public double CanvasScale()
        {
            Send("where");
            var line = WaitForLine("CANVASWIN ");
            Assert.NotNull(line);
            var at = line!.IndexOf(" scale=", StringComparison.Ordinal);
            Assert.True(at > 0, line);
            var end = line.IndexOf(' ', at + 1);
            return double.Parse(line[(at + 7)..(end < 0 ? line.Length : end)], CultureInfo.InvariantCulture);
        }

        public Dictionary<string, string> CanvasFields()
        {
            Send("where");
            var line = WaitForLine("CANVASWIN ");
            Assert.NotNull(line);
            var fields = new Dictionary<string, string>();
            foreach (var part in line!.Split(' '))
            {
                var equals = part.IndexOf('=');
                if (equals > 0)
                {
                    fields[part[..equals]] = part[(equals + 1)..];
                }
            }

            return fields;
        }

        public (int X, int Y, Box Screen, bool Deformed, string Home) CanvasWindow()
        {
            Send("where");
            var line = WaitForLine("CANVASWIN ");
            Assert.NotNull(line);
            var fields = new Dictionary<string, string>();
            foreach (var part in line!.Split(' '))
            {
                var equals = part.IndexOf('=');
                if (equals > 0)
                {
                    fields[part[..equals]] = part[(equals + 1)..];
                }
            }

            var canvas = fields["canvas"].Split(',');
            var screen = fields["screen"].Split(',');
            return (
                int.Parse(canvas[0], CultureInfo.InvariantCulture),
                int.Parse(canvas[1], CultureInfo.InvariantCulture),
                new Box(
                    int.Parse(screen[0], CultureInfo.InvariantCulture),
                    int.Parse(screen[1], CultureInfo.InvariantCulture),
                    int.Parse(fields["width"], CultureInfo.InvariantCulture),
                    int.Parse(fields["height"], CultureInfo.InvariantCulture)),
                fields["deformed"] == "yes",
                fields["home"]);
        }

        public string? Options
        {
            get
            {
                lock (_gate)
                {
                    return _options;
                }
            }
        }

        public Shot Shot(string name, int output = 0)
        {
            Thread.Sleep(400);
            var path = Path.Combine(_runtimeDir, $"{name}.png");
            Send(output == 0 ? $"shotraw {path}" : $"shotraw {path} {output}");
            var line = WaitForLine("SHOTRAW ");
            Assert.True(line is not null && line.StartsWith($"SHOTRAW {path}", StringComparison.Ordinal), line ?? "no SHOTRAW line");
            var (rgba, width, height) = PngCodec.Decode(File.ReadAllBytes(path));
            return new Shot(rgba, width, height);
        }

        public void Dispose()
        {
            try
            {
                if (!_compositor.HasExited)
                {
                    _compositor.StandardInput.WriteLine("quit");
                    _compositor.StandardInput.Flush();
                    _ = _compositor.WaitForExit(3000);
                }
            }
            catch (IOException)
            {
            }

            if (_client is { } client && !client.HasExited)
            {
                client.Kill(entireProcessTree: true);
            }

            foreach (var extra in _extra)
            {
                if (!extra.HasExited)
                {
                    extra.Kill(entireProcessTree: true);
                }

                extra.Dispose();
            }

            if (!_compositor.HasExited)
            {
                _compositor.Kill(entireProcessTree: true);
            }

            _compositor.Dispose();
            _client?.Dispose();
            try
            {
                Directory.Delete(_runtimeDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static bool ClientAvailable()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(directory, "weston-simple-shm")))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? Locate(string name)
        {
            var directory = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && directory is not null; i++)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            }

            return null;
        }
    }
}
