using System.Globalization;
using Xunit;
using static TinyComp.Tests.QuillFrameTests;

namespace TinyComp.Tests;

public sealed class InspectorProcessTests
{
    public static TheoryData<string> Rows => ["gl", "vulkan"];

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_inspector_toggles_shows_panels_and_moves_between_outputs(string row)
    {
        using var session = Start(row, outputs: 2);
        Assert.SkipWhen(session is null, $"tinycomp, ssdwin or the {row} row is not available beside the tests");

        var closed = State(session!, "inspector-state");
        Assert.Contains("open=no", closed);
        Assert.Contains("panels=Outputs", closed);

        var open = State(session!, "inspector toggle");
        Assert.Contains("open=yes", open);
        Assert.Contains("output=HEADLESS-1", open);
        Assert.Contains($"host={(row == "vulkan" ? "vulkan" : "gl")}", open);

        Assert.Contains("panels=Outputs,Windows", State(session!, "inspector-panel windows on"));
        Assert.Contains("panels=Windows", State(session!, "inspector-panel outputs off"));
        session!.Send("inspector-panel nope");
        Assert.NotNull(session.WaitForLine("ERR no inspector panel nope"));

        Assert.Contains("output=HEADLESS-2", State(session, "inspector-output HEADLESS-2"));
        session.Send("inspector-output NOWHERE");
        Assert.NotNull(session.WaitForLine("ERR no output NOWHERE"));

        Assert.Contains("open=no", State(session, "inspector close"));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_touch_on_a_panel_close_box_closes_the_panel(string row)
    {
        using var session = Start(row);
        Assert.SkipWhen(session is null, $"tinycomp, ssdwin or the {row} row is not available beside the tests");
        Assert.Contains("open=yes", State(session!, "inspector open"));
        Assert.Contains("panels=Outputs,Windows", State(session!, "inspector-panel windows on"));
        Thread.Sleep(600);
        session!.Send("ipc input/touch {\"kind\":\"down\",\"id\":1,\"x\":531,\"y\":50}");
        session.Send("ipc input/touch {\"kind\":\"frame\"}");
        Thread.Sleep(100);
        session.Send("ipc input/touch {\"kind\":\"up\",\"id\":1}");
        session.Send("ipc input/touch {\"kind\":\"frame\"}");
        Thread.Sleep(400);
        var state = State(session, "inspector-state");
        Assert.Contains("panels=Windows", state);
        Assert.DoesNotContain("Outputs", state);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Super_F12_toggles_the_inspector(string row)
    {
        using var session = Start(row);
        Assert.SkipWhen(session is null, $"tinycomp, ssdwin or the {row} row is not available beside the tests");
        Chord(session!);
        var line = session!.WaitForLine("INSPECTOR ");
        Assert.NotNull(line);
        Assert.Contains("open=yes", line);
        Chord(session);
        line = session.WaitForLine("INSPECTOR ");
        Assert.NotNull(line);
        Assert.Contains("open=no", line);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_gizmo_drag_turns_the_selected_window(string row)
    {
        using var session = Start(row);
        Assert.SkipWhen(session is null, $"tinycomp, ssdwin or the {row} row is not available beside the tests");
        var (x, y) = session!.WindowPosition();
        Assert.Contains("open=yes", State(session, "inspector open"));
        State(session, "inspector-panel outputs off");
        State(session, "inspector-panel transform on");
        Settle(session);

        var centerX = x + 128;
        var centerY = y + 128;
        var shot = session.Shot("gizmo");
        var radius = RingRadius(shot, centerX, centerY);
        Assert.True(radius > 10, $"no rotation ring right of the window's center at {centerX},{centerY}");

        var start = -Math.PI / 4;
        session.Send(Point(centerX, centerY, radius, start));
        Settle(session);
        session.Send("button 272 1");
        for (var step = 1; step <= 12; step++)
        {
            session.Send(Point(centerX, centerY, radius, start + (step * Math.PI / 24)));
            Settle(session);
        }

        session.Send("button 272 0");
        Settle(session);
        var line = session.Printed("ROTATE ");
        Assert.True(line is not null, "the drag printed no ROTATE line");
        var degrees = double.Parse(line!.Split("angle=")[1], CultureInfo.InvariantCulture);
        Assert.InRange(Math.Abs(degrees), 60, 120);
    }

    [Fact]
    public void A_reload_applies_the_inspector_table()
    {
        using var session = QuillSession.Start("gl", config: "[inspector]\nopen = true\npanels = [\"memory\", \"scene\"]\n");
        Assert.SkipWhen(session is null, "tinycomp, ssdwin or the gl row is not available beside the tests");
        var state = State(session!, "inspector-state");
        Assert.Contains("open=yes", state);
        Assert.Contains("panels=Scene,Memory", state);

        session!.Rewrite("[inspector]\nenabled = false\n");
        session.Send("reload");
        Assert.NotNull(session.WaitForLine("RELOAD "));
        session.Send("inspector-state");
        Assert.Contains("declined=\"[inspector] enabled is false\"", session.WaitForLine("INSPECTOR ") ?? string.Empty);
        session.Send("inspector open");
        Assert.NotNull(session.WaitForLine("ERR the inspector is unavailable: [inspector] enabled is false"));
    }

    private static QuillSession? Start(string row, int outputs = 1) =>
        QuillSession.Start(row, config: string.Empty, extra: ["--outputs", outputs.ToString(CultureInfo.InvariantCulture)]);

    private static string State(QuillSession session, string command)
    {
        session.Send(command);
        var line = session.WaitForLine("INSPECTOR ");
        Assert.True(line is not null, $"'{command}' printed no INSPECTOR line");
        return line!;
    }

    private static string Point(int centerX, int centerY, int radius, double angle) => string.Create(
        CultureInfo.InvariantCulture,
        $"move {centerX + (radius * Math.Cos(angle)):F0} {centerY + (radius * Math.Sin(angle)):F0}");

    private static void Chord(QuillSession session)
    {
        session.Send("key 125 1");
        session.Send("key 88 1");
        session.Send("key 88 0");
        session.Send("key 125 0");
    }

    private static void Settle(QuillSession session)
    {
        try
        {
            session.Send("settle");
        }
        catch (IOException)
        {
            Assert.Fail($"the compositor exited:\n{session.Errors()}");
        }

        Assert.True(session.WaitForLine("OK session/settle") is not null, $"no settle:\n{session.Errors()}");
    }

    private static int RingRadius(QuillFrameTests.Shot shot, int centerX, int centerY)
    {
        for (var dx = 12; centerX + dx < shot.Width && dx < 400; dx++)
        {
            var i = ((centerY * shot.Width) + centerX + dx) * 4;
            var (r, g, b) = (shot.Rgba[i], shot.Rgba[i + 1], shot.Rgba[i + 2]);
            if (b > 140 && r < 40 && g < 40)
            {
                return dx + 1;
            }
        }

        return 0;
    }
}
