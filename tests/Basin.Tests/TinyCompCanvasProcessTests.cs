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
        private readonly List<Process> _extra = [];
        private string _socket = string.Empty;
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
            string[]? clientArguments = null)
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
                        Monitor.PulseAll(session._gate);
                    }
                }
            };
            compositor.ErrorDataReceived += (_, _) => { };
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

        public Shot Shot(string name)
        {
            Thread.Sleep(400);
            var path = Path.Combine(_runtimeDir, $"{name}.png");
            Send($"shotraw {path}");
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
