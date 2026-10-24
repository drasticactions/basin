using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Basin.Effects;
using Xunit;
using static TinyComp.Tests.CanvasRows;

namespace TinyComp.Tests;

public sealed class CanvasScaleTests
{
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
        Assert.NotNull(session.WaitForClientLine("PANEL "));
        Assert.NotNull(session.WaitForClientLine("PANEL "));
        session.Send("park left");
        Assert.Contains("to=" + (60 + ZoneWidth - 640), session.WaitForLine("PARK "));
        Assert.Equal(60 + ZoneWidth - 640, session.WaitForWindowX(60 + ZoneWidth - 640).X);
        session.Send("park up");
        var corner = session.WaitForLine("PARK ");
        var rimY = (int)Math.Round(30 + ZoneHeight - (360 / Math.Sqrt(2)));
        Assert.Contains($" y={rimY} corner", corner);
        var parked = session.Shot("docked");
        Assert.Equal(0, Saturated(parked, 0, 0, 60, parked.Height));
        Assert.Equal(0, Saturated(parked, 0, 0, parked.Width, 30));
        Assert.True(Saturated(parked, 60, 30, ZoneWidth, ZoneHeight) > 0, "the client draws in the corner inside both panels");
    }
}
