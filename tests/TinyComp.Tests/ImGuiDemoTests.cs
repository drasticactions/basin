using Xunit;
using static TinyComp.Tests.QuillFrameTests;

namespace TinyComp.Tests;

public sealed class ImGuiDemoTests
{
    private const int CheckboxX = 58;
    private const int CheckboxY = 79;

    private static readonly (int X, int Y, int Width, int Height)[] DemoWindows =
    [
        (40, 40, 280, 110),
        (40, 170, 420, 500),
        (480, 40, 560, 400),
        (480, 460, 560, 240),
    ];

    public static TheoryData<string> Rows => ["gl", "vulkan", "skia-vulkan"];

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_click_on_the_demo_checkbox_checks_it(string row)
    {
        using var session = Start(row);
        Assert.SkipWhen(session is null, $"tinycomp, ssdwin or the {row} row is not available beside the tests");
        Assert.True(session!.Logged("imgui demo: Dear ImGui"), $"the {row} row built no imgui demo");

        var before = session.Shot("before");
        session.Send("move 700 710");
        session.Send($"move {CheckboxX} {CheckboxY}");
        session.Send("button 272 1");
        session.Send("button 272 0");
        Assert.NotNull(Settle(session));
        var after = session.Shot("after");

        var was = CheckMarks(before);
        var now = CheckMarks(after);
        Assert.True(was == 0, $"the box shows {was} check-mark pixels before the click");
        Assert.True(now > 6, $"the box shows {now} check-mark pixels after the click");
        Assert.False(session.ClientPrinted("BUTTON"), "the click inside the overlay reached the client");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_click_outside_every_imgui_window_reaches_the_client(string row)
    {
        using var session = Start(row);
        Assert.SkipWhen(session is null, $"tinycomp, ssdwin or the {row} row is not available beside the tests");
        var shot = session!.Shot("layout");
        var (x, y) = session.WindowPosition();
        var target = Uncovered(x, y, 256, 256, shot.Width, shot.Height);
        Assert.SkipWhen(target is null, $"ssdwin at {x},{y} sits wholly under the demo windows on a {shot.Width}x{shot.Height} output");

        session.Send($"move {target!.Value.X} {target.Value.Y}");
        session.Send("button 272 1");
        session.Send("button 272 0");
        Settle(session);
        Assert.True(
            WaitForClient(session, "BUTTON"),
            $"a click at {target.Value.X},{target.Value.Y}, outside every imgui window, never reached ssdwin");
    }

    private static QuillSession? Start(string row) =>
        QuillSession.Start(row, config: string.Empty, extra: ["--imgui-demo"]);

    private static string? Settle(QuillSession session)
    {
        session.Send("settle");
        return session.WaitForLine("OK session/settle");
    }

    private static bool WaitForClient(QuillSession session, string fragment)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline)
        {
            if (session.ClientPrinted(fragment))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static (int X, int Y)? Uncovered(int x, int y, int width, int height, int outputWidth, int outputHeight)
    {
        for (var py = y + 16; py < y + height - 8; py += 8)
        {
            for (var px = x + 8; px < x + width - 8; px += 8)
            {
                if (px < 0 || py < 0 || px >= outputWidth || py >= outputHeight)
                {
                    continue;
                }

                var covered = false;
                foreach (var (wx, wy, ww, wh) in DemoWindows)
                {
                    if (px >= wx - 4 && py >= wy - 4 && px < wx + ww + 4 && py < wy + wh + 4)
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    return (px, py);
                }
            }
        }

        return null;
    }

    private static int CheckMarks(QuillFrameTests.Shot shot)
    {
        var count = 0;
        for (var y = CheckboxY - 9; y <= CheckboxY + 9; y++)
        {
            for (var x = CheckboxX - 9; x <= CheckboxX + 9; x++)
            {
                var pixel = Pixel(shot, x, y);
                if (Math.Abs(Red(pixel) - 0x42) < 24 && Math.Abs((int)((pixel >> 8) & 0xff) - 0x96) < 24 && Blue(pixel) > 0xd8)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static uint Pixel(QuillFrameTests.Shot shot, int x, int y)
    {
        var i = ((y * shot.Width) + x) * 4;
        return (uint)((shot.Rgba[i] << 16) | (shot.Rgba[i + 1] << 8) | shot.Rgba[i + 2]);
    }

    private static int Red(uint pixel) => (int)((pixel >> 16) & 0xff);

    private static int Blue(uint pixel) => (int)(pixel & 0xff);
}
