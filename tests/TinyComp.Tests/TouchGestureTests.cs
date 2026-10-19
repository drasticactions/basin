using System.Globalization;
using System.Text.Json;
using Basin;
using Xunit;
using static TinyComp.Tests.PhysicsRows;

namespace TinyComp.Tests;

public sealed class TouchGestureTests
{
    [Theory]
    [MemberData(nameof(PhysicsRows.Engines), MemberType = typeof(PhysicsRows))]
    public void A_touch_drag_on_the_titlebar_released_while_moving_throws_the_window(string engine)
    {
        var ssdwin = CanvasRows.SsdWin();
        using var session = ssdwin is null ? null : CanvasSession.Start(PhysicsOn(engine), ssdwin, ["server"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var (x, y) = Titlebar(session!);
        var (startX, _) = session!.WindowPosition();
        _ = session.WaitForLine("PHYS ");
        var lines = new List<string>();
        var start = (uint)Environment.TickCount;
        for (var i = 0; i <= 10; i++)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"ipc input/touch {{\"kind\":\"{(i == 0 ? "down" : "motion")}\",\"id\":1,\"x\":{x + (i * 20):F2},\"y\":{y:F2},\"time_ms\":{start + (i * 8)}}}"));
            lines.Add("ipc input/touch {\"kind\":\"frame\"}");
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"ipc input/touch {{\"kind\":\"up\",\"id\":1,\"time_ms\":{start + 88}}}"));
        lines.Add("ipc input/touch {\"kind\":\"frame\"}");
        session.SendPaced(lines, 40);

        var flying = Body(session);
        Assert.Equal("yes", flying.GetValueOrDefault("awake"));
        Assert.True(Number(flying, "vx") > 200, $"released at vx={flying.GetValueOrDefault("vx")}");
        _ = Rest(session);
        var (restX, _) = session.WindowPosition();
        Assert.True(restX > startX + 260, $"the throw carried the window from {startX} to {restX}");
    }

    [Fact]
    public void A_two_finger_twist_on_the_titlebar_turns_the_window_and_holds()
    {
        var ssdwin = CanvasRows.SsdWin();
        using var session = ssdwin is null ? null : CanvasSession.Start(string.Empty, ssdwin, ["server"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var (x, y) = Titlebar(session!);

        session!.SendPaced(TouchTwist(x, y, 50, 30), 8);
        Assert.StartsWith("TWIST ", session.WaitForLine("TWIST "));
        Thread.Sleep(1000);
        var tilt = Tilt(session);

        Assert.NotNull(tilt);
        Assert.InRange(Number(tilt!, "angle"), 29, 31);
        Assert.Equal("yes", tilt["held"]);
        Assert.Equal("no", tilt["spinning"]);
    }

    [Fact]
    public void A_three_finger_twist_on_the_client_turns_the_window()
    {
        var ssdwin = CanvasRows.SsdWin();
        using var session = ssdwin is null ? null : CanvasSession.Start(string.Empty, ssdwin, ["none"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var (x, y) = session!.WindowPosition();

        session.SendPaced(TouchTwist(x + 128, y + 128, 60, -40, fingers: 3), 8);
        Assert.StartsWith("TWIST ", session.WaitForLine("TWIST "));
        Thread.Sleep(1000);
        var tilt = Tilt(session);

        Assert.NotNull(tilt);
        Assert.InRange(Number(tilt!, "angle"), -41, -39);
    }

    [Theory]
    [InlineData(true, "off")]
    [InlineData(false, "off")]
    [InlineData(true, "bepu")]
    public void Two_fingers_move_a_window_only_after_a_hold(bool hold, string engine)
    {
        var ssdwin = CanvasRows.SsdWin();
        var config = engine == "off" ? string.Empty : PhysicsOn(engine);
        using var session = ssdwin is null ? null : CanvasSession.Start(config, ssdwin, ["none"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var (startX, startY) = session!.WindowPosition();
        var (ax, bx, y) = (startX + 90, startX + 150, startY + 128);

        string Touch(string kind, int id, double x) =>
            string.Create(CultureInfo.InvariantCulture, $"ipc input/touch {{\"kind\":\"{kind}\",\"id\":{id},\"x\":{x},\"y\":{y}}}");

        session.SendPaced([Touch("down", 1, ax), Touch("down", 2, bx), "ipc input/touch {\"kind\":\"frame\"}"], 8);
        if (hold)
        {
            Thread.Sleep(450);
        }

        var slide = new List<string>();
        for (var i = 1; i <= 10; i++)
        {
            slide.Add(Touch("motion", 1, ax + (i * 20)));
            slide.Add(Touch("motion", 2, bx + (i * 20)));
            slide.Add("ipc input/touch {\"kind\":\"frame\"}");
        }

        slide.Add(Touch("up", 1, 0).Replace(",\"x\":0", string.Empty, StringComparison.Ordinal));
        slide.Add("ipc input/touch {\"kind\":\"up\",\"id\":2}");
        slide.Add("ipc input/touch {\"kind\":\"frame\"}");
        session.SendPaced(slide, 30);
        Thread.Sleep(1500);

        var began = session.WaitForLine("HOLDMOVE ", hold ? 1000 : 500);
        var (endX, _) = session.WindowPosition();
        if (hold)
        {
            Assert.StartsWith("HOLDMOVE ", began);
            Assert.True(endX >= startX + 180, $"the hold-move carried the window from {startX} to {endX}");
        }
        else
        {
            Assert.Null(began);
            Assert.Equal(startX, endX);
        }
    }

    [Theory]
    [InlineData("off")]
    [InlineData("bepu")]
    public void A_held_two_finger_slide_that_turns_moves_and_rotates_the_window(string engine)
    {
        var ssdwin = CanvasRows.SsdWin();
        var config = engine == "off" ? string.Empty : PhysicsOn(engine);
        using var session = ssdwin is null ? null : CanvasSession.Start(config, ssdwin, ["none"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var (startX, startY) = session!.WindowPosition();
        var (grabX, grabY) = (startX + 128.0, startY + 128.0);

        string Touch(string kind, int id, double x, double y) =>
            string.Create(CultureInfo.InvariantCulture, $"ipc input/touch {{\"kind\":\"{kind}\",\"id\":{id},\"x\":{x:F2},\"y\":{y:F2}}}");

        session.SendPaced([Touch("down", 1, grabX - 30, grabY), Touch("down", 2, grabX + 30, grabY), "ipc input/touch {\"kind\":\"frame\"}"], 8);
        Thread.Sleep(450);
        var lines = new List<string>();
        const int Steps = 15;
        for (var i = 1; i <= Steps; i++)
        {
            var angle = (Math.PI / 6) * i / Steps;
            var (cx, cy) = (grabX + (i * 20), grabY + (i * 6));
            lines.Add(Touch("motion", 1, cx - (30 * Math.Cos(angle)), cy - (30 * Math.Sin(angle))));
            lines.Add(Touch("motion", 2, cx + (30 * Math.Cos(angle)), cy + (30 * Math.Sin(angle))));
            lines.Add("ipc input/touch {\"kind\":\"frame\"}");
        }

        session.SendPaced(lines, 16);
        Thread.Sleep(300);
        session.SendPaced(["ipc input/touch {\"kind\":\"up\",\"id\":1}", "ipc input/touch {\"kind\":\"up\",\"id\":2}", "ipc input/touch {\"kind\":\"frame\"}"], 8);

        var end = session.WaitForLine("HOLDMOVE end", 2000);
        Assert.NotNull(end);
        Assert.InRange(Number(Fields(end!), "angle"), 28, 32);
        Thread.Sleep(1500);
        var tilt = Tilt(session);
        Assert.NotNull(tilt);
        Assert.InRange(Number(tilt!, "angle"), 28, 32);
        Assert.Equal("yes", tilt["held"]);

        var (endX, endY) = session.WindowPosition();
        var pivot = (endX + 128 - (grabX + (Steps * 20)), endY + 128 - (grabY + (Steps * 6)));
        Assert.True(Math.Abs(pivot.Item1) <= 8 && Math.Abs(pivot.Item2) <= 8, $"the grabbed center drifted {pivot} from the fingers");
    }

    [Fact]
    public void A_twist_on_the_client_stays_with_the_client()
    {
        var ssdwin = CanvasRows.SsdWin();
        using var session = ssdwin is null ? null : CanvasSession.Start(string.Empty, ssdwin, ["server"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var (x, _) = session!.WindowPosition();

        session.SendPaced(TouchTwist(x + 128, 198, 50, 40), 8);
        Thread.Sleep(500);
        Assert.Null(Tilt(session));
    }

    [Fact]
    public void A_spun_release_keeps_turning_until_stop_spin_catches_it()
    {
        var ssdwin = CanvasRows.SsdWin();
        using var session = ssdwin is null
            ? null
            : CanvasSession.Start("[effects]\nrotate_release = \"spin\"\n", ssdwin, ["server"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var (x, y) = Titlebar(session!);

        session!.SendPaced(TouchTwist(x, y, 50, 30, steps: 16), 8);
        session.WaitForLine("TWIST ");
        session.Send("stop-spin");
        var stop = session.WaitForLine("STOPSPIN ");
        Assert.NotNull(stop);
        Assert.DoesNotContain(" idle", stop, StringComparison.Ordinal);

        var caught = Number(Tilt(session)!, "angle");
        Assert.True(caught > 30.5, $"the spin carried past the fingers to {caught}");
        Thread.Sleep(600);
        var after = Tilt(session)!;
        Assert.Equal(caught, Number(after, "angle"), 2);
        Assert.Equal("no", after["spinning"]);
    }
}
