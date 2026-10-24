using System.Globalization;
using System.Text.Json;
using Basin;
using Xunit;
using static TinyComp.Tests.PhysicsRows;

namespace TinyComp.Tests;

public sealed class PhysicsThrowTests
{
    [Theory]
    [MemberData(nameof(PhysicsRows.Engines), MemberType = typeof(PhysicsRows))]
    public void A_throw_comes_to_rest_integral_and_upright(string engine)
    {
        using var session = CanvasSession.Start(PhysicsOn(engine));
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var (startX, startY) = session!.WindowPosition();

        session.Send("throw 2500 600");
        Assert.StartsWith("THROW ", session.WaitForLine("THROW "));
        var rest = Rest(session);

        Assert.Equal("no", rest["awake"]);
        Assert.Equal(0, Number(rest, "angle"));
        var (x, y) = session.WindowPosition();
        Assert.True(x > startX + 200, $"the window moved from {startX} to {x}");
        Assert.True(y > startY, $"the window moved from {startY} to {y}");
        Assert.InRange(x + Number(rest, "w"), 0, 1280);
    }

    [Theory]
    [MemberData(nameof(PhysicsRows.Engines), MemberType = typeof(PhysicsRows))]
    public void A_pointer_drag_throws_with_the_release_velocity(string engine)
    {
        using var session = CanvasSession.Start(PhysicsOn(engine));
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var (startX, startY) = session!.WindowPosition();
        var grabX = startX + 80;
        var grabY = startY + 80;
        var drag = new List<string> { $"move {grabX} {grabY}", "key 56 1", "button 272 1" };
        for (var i = 1; i <= 12; i++)
        {
            drag.Add($"move {grabX + (i * 20)} {grabY + (i * 4)}");
        }

        drag.Add("button 272 0");
        drag.Add("key 56 0");
        session.SendPaced(drag, 8);
        _ = Rest(session);

        var (x, _) = session.WindowPosition();
        Assert.True(x > startX + 240 + 100, $"released at {startX + 240}, came to rest at {x}");
    }

    [Theory]
    [MemberData(nameof(PhysicsRows.Engines), MemberType = typeof(PhysicsRows))]
    public void Gravity_pulls_a_window_to_the_floor_and_config_restores_it(string engine)
    {
        using var session = CanvasSession.Start(PhysicsOn(engine));
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");

        session.Send("gravity 3000");
        Assert.Equal("GRAVITY 3000", session.WaitForLine("GRAVITY "));
        session.Send("throw 0 1");
        var rest = Rest(session);
        var (_, y) = session.WindowPosition();
        Assert.InRange(y + Number(rest, "h"), 718, 720);

        session.Send("gravity config");
        Assert.Equal("GRAVITY config", session.WaitForLine("GRAVITY "));
    }

    [Theory]
    [MemberData(nameof(PhysicsRows.Engines), MemberType = typeof(PhysicsRows))]
    public void A_frozen_world_holds_a_thrown_window(string engine)
    {
        using var session = CanvasSession.Start(PhysicsOn(engine));
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        var start = session!.WindowPosition();

        session.Send("freeze on");
        Assert.Equal("PHYSICS frozen=yes", session.WaitForLine("PHYSICS "));
        session.Send("throw 3000 0");
        Thread.Sleep(500);
        Assert.Equal(start, session.WindowPosition());

        session.Send("freeze off");
        _ = Rest(session);
        Assert.NotEqual(start, session.WindowPosition());
    }

    [Theory]
    [MemberData(nameof(PhysicsRows.Engines), MemberType = typeof(PhysicsRows))]
    public void A_click_on_a_rotated_window_reaches_the_client_at_its_surface_coordinates(string engine)
    {
        var ssdwin = CanvasRows.SsdWin();
        using var session = ssdwin is null ? null : CanvasSession.Start(PhysicsOn(engine), ssdwin, ["none"]);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");

        session!.Send("throw 200 0 3");
        session.WaitForLine("THROW ");
        for (var attempt = 0; attempt < 80 && Math.Abs(Number(Body(session), "angle")) <= 0.05; attempt++)
        {
            Thread.Sleep(25);
        }

        session.Send("freeze on");
        Assert.Equal("PHYSICS frozen=yes", session.WaitForLine("PHYSICS "));
        var body = Body(session);
        var angle = Number(body, "angle");
        Assert.True(Math.Abs(angle) > 0.05, $"the window turned {angle} before the freeze");

        var (x, y) = session.WindowPosition();
        var half = Number(body, "w") / 2;
        foreach (var (dx, dy) in new[] { (60.0, 0.0), (-30.0, 70.0), (40.0, -50.0) })
        {
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var screenX = Math.Round(x + half + (cos * dx) - (sin * dy), 2);
            var screenY = Math.Round(y + half + (sin * dx) + (cos * dy), 2);
            session.Send(string.Create(CultureInfo.InvariantCulture, $"move {screenX} {screenY}"));
            session.Send("button 272 1");
            session.Send("button 272 0");
            var line = session.WaitForClientLine("BUTTON ");
            Assert.NotNull(line);
            var parts = line!.Split(' ');
            var localX = double.Parse(parts[2], CultureInfo.InvariantCulture);
            var localY = double.Parse(parts[3], CultureInfo.InvariantCulture);
            Assert.True(
                Math.Abs(localX - (half + dx)) < 1.0 && Math.Abs(localY - (half + dy)) < 1.0,
                $"angle {angle}: clicked {dx},{dy} from the center, the client saw {localX},{localY}");
        }
    }

    [Fact]
    public void A_reload_that_changes_the_engine_rebinds_the_windows_where_they_rest()
    {
        using var session = CanvasSession.Start(PhysicsOn("box2d"));
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        session!.Send("throw 900 0");
        session.WaitForLine("THROW ");
        _ = Rest(session);
        var before = session.WindowPosition();
        session.Send("physics-state");
        Assert.Contains("engine=box2d", session.WaitForLine("PHYSICS "));

        session.Rewrite(PhysicsOn("bepu"));
        session.Send("reload");
        Assert.Contains("physics-engine=bepu", session.WaitForLine("RELOAD "));
        session.Send("physics-state");
        var state = session.WaitForLine("PHYSICS ");
        Assert.Contains("engine=bepu", state);
        Assert.Contains("bodies=1", state);
        Assert.Equal(before, session.WindowPosition());

        session.Send("throw 900 0");
        session.WaitForLine("THROW ");
        _ = Rest(session);
        Assert.NotEqual(before, session.WindowPosition());
    }

    [Theory]
    [MemberData(nameof(PhysicsRows.Engines), MemberType = typeof(PhysicsRows))]
    public void Physics_state_reports_every_body_as_json(string engine)
    {
        using var session = CanvasSession.Start(PhysicsOn(engine));
        Assert.SkipWhen(session is null, "tinycomp or weston-simple-shm is not available beside the tests");
        session!.Send("throw 10 0");
        session.WaitForLine("THROW ");

        session.Send("ipc tinycomp/physics-state {}");
        var line = session.WaitForLine("OK tinycomp/physics-state ");
        Assert.NotNull(line);
        Assert.Contains("enabled=true", line, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"dynamic\"", line, StringComparison.Ordinal);
    }
}
