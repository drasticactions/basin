using Xunit;

namespace TinyComp.Tests;

public sealed class FullscreenRoundTripTests
{
    private const string Config = "[frame]\nstyle = \"flat\"\n\n[overview]\nhot_corner = \"none\"\n";

    [Fact]
    public void A_fullscreen_window_floated_out_of_the_layout_keeps_the_fullscreen_size()
    {
        using var session = Start(fullscreen: true);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var full = LastTrace(session!);
        Assert.True(full.Fullscreen);

        Run(session!, "float", "RETILED ");
        var mark = Traces(session!).Count;
        Run(session!, "float", "FLOATED ", "size=keep");
        var after = WaitForTrace(session!, mark, trace => !trace.Fullscreen);

        Assert.All(after, trace => Assert.Equal((full.Width, full.Height), (trace.Width, trace.Height)));
    }

    [Fact]
    public void A_floated_fullscreen_window_tiled_again_is_fullscreen_again_with_no_windowed_size()
    {
        using var session = Start(fullscreen: true);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var full = LastTrace(session!);
        var mark = Traces(session!).Count;

        Run(session!, "float", "RETILED ");
        Run(session!, "float", "FLOATED ", "size=keep");
        WaitForTrace(session!, mark, trace => !trace.Fullscreen);
        Run(session!, "float", "RETILED ", "fullscreen=True");
        var after = WaitForTrace(session!, mark, trace => trace.Fullscreen);

        Assert.All(after, trace => Assert.Equal((full.Width, full.Height), (trace.Width, trace.Height)));
    }

    [Fact]
    public void A_window_that_left_fullscreen_itself_tiles_as_a_normal_window()
    {
        using var session = Start(fullscreen: true);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        var mark = Traces(session!).Count;

        Run(session!, "float", "RETILED ");
        Run(session!, "float", "FLOATED ", "size=keep");
        WaitForTrace(session!, mark, trace => !trace.Fullscreen);
        Run(session!, "fullscreen off", "FULLSCREEN ");
        Run(session!, "float", "RETILED ", "fullscreen=False");
        Thread.Sleep(300);

        Assert.False(LastTrace(session!).Fullscreen);
    }

    [Fact]
    public void A_fullscreen_exit_carries_the_restored_size_in_its_first_configure()
    {
        using var session = Start(fullscreen: false);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");

        Run(session!, "fullscreen on", "FULLSCREEN ");
        WaitForTrace(session!, 0, trace => trace.Fullscreen);
        var mark = Traces(session!).Count;
        Run(session!, "fullscreen off", "FULLSCREEN ");
        var after = WaitForTrace(session!, mark, trace => !trace.Fullscreen);

        var first = after.Find(trace => !trace.Fullscreen);
        Assert.Equal((256, 256), (first.Width, first.Height));
    }

    [Fact]
    public void A_fullscreen_x_window_floated_and_tiled_again_never_sees_another_size()
    {
        var x11focus = Path.Combine(RepoRoot(), "scripts", "wlclients", "bin", "x11focus");
        var log = Path.Combine("/tmp", $"x11focus-{Guid.NewGuid():N}.log");
        using var session = EmulationTests.Session.Start(
            "0x0", 0, Config, $"weston-simple-shm & sleep 1; exec '{x11focus}' fullscreen 60 > '{log}'");
        Assert.SkipWhen(session is null || !File.Exists(x11focus) || !OnPath("weston-simple-shm"), "tinycomp, Xwayland, x11focus or weston-simple-shm is not available");
        try
        {
            var full = XWin(session!, "x11focus");
            Assert.NotEqual((200, 150), (full.Width, full.Height));

            session!.Send("tile");
            Assert.NotNull(session.WaitForLine("TILED "));
            session.Send("float");
            Assert.Contains("size=keep", session.WaitForLine("FLOATED x11focus"), StringComparison.Ordinal);
            session.Send("float");
            Assert.Contains("fullscreen=True", session.WaitForLine("RETILED x11focus"), StringComparison.Ordinal);
            Assert.Equal((full.Width, full.Height), Size(XWin(session, "x11focus")));

            var sizes = File.ReadAllLines(log)
                .Where(line => line.StartsWith("CONFIGURE ", StringComparison.Ordinal))
                .Select(line => line.Split(' '))
                .Select(parts => (int.Parse(parts[3]), int.Parse(parts[4])))
                .ToList();
            Assert.NotEmpty(sizes);
            Assert.All(sizes, size => Assert.Equal((full.Width, full.Height), size));
        }
        finally
        {
            File.Delete(log);
        }
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(sourcePath)))!;

    private static (int X, int Y, int Width, int Height) XWin(EmulationTests.Session session, string name)
    {
        string? line = null;
        for (var i = 0; i < 40 && line is null; i++)
        {
            session.Send("where");
            line = session.WaitForLine($"XWIN {name}", 500);
        }

        Assert.NotNull(line);
        while (session.WaitForLine($"XWIN {name}", 300) is { } later)
        {
            line = later;
        }

        var parts = line!.Split(' ');
        var size = parts[4].Split('x');
        return (int.Parse(parts[2]), int.Parse(parts[3]), int.Parse(size[0]), int.Parse(size[1]));
    }

    private static (int Width, int Height) Size((int X, int Y, int Width, int Height) window) => (window.Width, window.Height);

    private static bool OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':').Any(dir => File.Exists(Path.Combine(dir, program)));

    private static QuillFrameTests.QuillSession? Start(bool fullscreen) =>
        QuillFrameTests.QuillSession.Start(
            "pixman", Config, fullscreen ? ["server", "fullscreen", "trace"] : ["server", "trace"]);

    private static void Run(QuillFrameTests.QuillSession session, string command, string reply, string? fragment = null)
    {
        session.Send(command);
        var line = session.WaitForLine(reply);
        Assert.NotNull(line);
        if (fragment is not null)
        {
            Assert.Contains(fragment, line, StringComparison.Ordinal);
        }
    }

    private static List<(int Width, int Height, bool Fullscreen)> Traces(QuillFrameTests.QuillSession session) =>
        session.ClientLines("TRACE ").ConvertAll(line =>
        {
            var parts = line.Split(' ');
            return (int.Parse(parts[1]), int.Parse(parts[2]), parts[3] == "1");
        });

    private static (int Width, int Height, bool Fullscreen) LastTrace(QuillFrameTests.QuillSession session)
    {
        var traces = Traces(session);
        Assert.NotEmpty(traces);
        return traces[^1];
    }

    private static List<(int Width, int Height, bool Fullscreen)> WaitForTrace(
        QuillFrameTests.QuillSession session, int from, Func<(int Width, int Height, bool Fullscreen), bool> until)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < deadline)
        {
            var traces = Traces(session);
            if (traces.Count > from && until(traces[^1]))
            {
                Thread.Sleep(200);
                return Traces(session)[from..];
            }

            Thread.Sleep(50);
        }

        Assert.Fail("the client saw no matching configure within 10 s");
        return [];
    }
}
