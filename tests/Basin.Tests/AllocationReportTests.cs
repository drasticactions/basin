using System.Globalization;
using Basin.Cli;
using Xunit;

namespace Basin.Tests;

public sealed class AllocationReportTests
{
    private static readonly string[] Fields =
        ["bytes=", "thread=", "per-frame=", "gen0=", "gen1=", "gen2=", "gc="];

    [Fact]
    public void The_report_names_every_field()
    {
        var line = Line(Run(["--alloc-report"], frames: 0));
        Assert.NotNull(line);

        foreach (var field in Fields)
        {
            Assert.Contains(field, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_run_that_counted_no_frames_reports_no_rate()
    {
        var line = Line(Run(["--alloc-report"], frames: 0));
        Assert.Contains("per-frame=n/a", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_counted_frames_divides_the_thread_figure_by_them()
    {
        var line = Line(Run(["--alloc-report"], frames: 8));
        Assert.NotNull(line);

        var thread = long.Parse(Field(line, "thread="), CultureInfo.InvariantCulture);
        var perFrame = long.Parse(Field(line, "per-frame="), CultureInfo.InvariantCulture);

        Assert.Equal(thread / 8, perFrame);
    }

    [Fact]
    public void The_collector_names_itself()
    {
        var line = Line(Run(["--alloc-report"], frames: 0));
        Assert.NotEmpty(Field(line!, "gc="));
    }

    [Fact]
    public void Nothing_is_reported_unless_it_is_asked_for()
    {
        Assert.Null(Line(Run([], frames: 0)));
    }

    [Fact]
    public void The_report_comes_after_the_body_has_finished()
    {
        var output = Run(["--alloc-report"], frames: 0);
        var body = Array.FindIndex(output, text => text == "BODY");
        var report = Array.FindIndex(output, text => text.StartsWith("ALLOC ", StringComparison.Ordinal));

        Assert.True(body >= 0);
        Assert.True(report > body);
    }

    private static string[] Run(string[] args, long frames)
    {
        var command = new BasinCommand("a command that measures itself");
        var writer = new StringWriter();
        var previous = Console.Out;

        Console.SetOut(writer);
        try
        {
            var status = command.Run(args, _ =>
            {
                Console.WriteLine("BODY");
                command.ReportFrames(frames);
                return 0;
            });

            Assert.Equal(0, status);
        }
        finally
        {
            Console.SetOut(previous);
        }

        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? Line(string[] output) =>
        Array.Find(output, text => text.StartsWith("ALLOC ", StringComparison.Ordinal));

    private static string Field(string line, string key)
    {
        var start = line.IndexOf(key, StringComparison.Ordinal);
        Assert.True(start >= 0);

        start += key.Length;
        var end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }
}
