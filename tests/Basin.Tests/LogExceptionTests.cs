using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class LogExceptionTests
{
    private static readonly BasinLogger Log = BasinLog.For("exception-test");

    private sealed class CollectingSink : IBasinLogSink
    {
        public List<(BasinLogLevel Level, string Category, string Text)> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            Lines.Add((level, category, message.ToString()));
    }

    private static T WithSink<T>(BasinLogLevel level, Func<CollectingSink, T> body)
    {
        var previousSink = BasinLog.Sink;
        var previousLevel = BasinLog.Level;
        var sink = new CollectingSink();
        BasinLog.Sink = sink;
        BasinLog.Level = level;
        try
        {
            return body(sink);
        }
        finally
        {
            BasinLog.Sink = previousSink;
            BasinLog.Level = previousLevel;
        }
    }

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    [Fact]
    public void The_line_ends_with_the_type_and_the_text()
    {
        var lines = WithSink(BasinLogLevel.Info, sink =>
        {
            var path = "/tmp/x.toml";
            Log.Warn(new IOException("Access denied."), $"config: {path} not read, defaults kept");
            return sink.Lines;
        });

        var line = Assert.Single(lines);
        Assert.Equal(BasinLogLevel.Warn, line.Level);
        Assert.Equal("exception-test", line.Category);
        Assert.Equal("config: /tmp/x.toml not read, defaults kept (IOException: Access denied.)", line.Text);
    }

    [Fact]
    public void Each_inner_exception_follows_on_the_same_line()
    {
        var lines = WithSink(BasinLogLevel.Info, sink =>
        {
            var inner = new UnauthorizedAccessException("No permission.");
            Log.Error(new InvalidOperationException("Save failed.", new IOException("Write failed.", inner)), $"settings: save failed");
            return sink.Lines;
        });

        var line = Assert.Single(lines);
        Assert.Equal(
            "settings: save failed (InvalidOperationException: Save failed.) <- (IOException: Write failed.) <- (UnauthorizedAccessException: No permission.)",
            line.Text);
    }

    [Fact]
    public void The_stack_trace_follows_only_at_debug()
    {
        var exception = Thrown(new InvalidOperationException("Broken."));
        var info = WithSink(BasinLogLevel.Info, sink =>
        {
            Log.Warn(exception, $"area: failed");
            return sink.Lines;
        });
        var debug = WithSink(BasinLogLevel.Debug, sink =>
        {
            Log.Warn(exception, $"area: failed");
            return sink.Lines;
        });

        Assert.DoesNotContain('\n', Assert.Single(info).Text);
        var text = Assert.Single(debug).Text;
        Assert.StartsWith("area: failed (InvalidOperationException: Broken.)" + Environment.NewLine, text);
        Assert.Contains(nameof(Thrown), text);
    }

    [Fact]
    public void The_static_form_writes_with_no_category()
    {
        var lines = WithSink(BasinLogLevel.Trace, sink =>
        {
            var exception = new FormatException("Bad.");
            BasinLog.Trace(exception, $"t");
            BasinLog.Debug(exception, $"d");
            BasinLog.Info(exception, $"i");
            BasinLog.Warn(exception, $"w");
            BasinLog.Error(exception, $"e");
            return sink.Lines;
        });

        Assert.Equal(
            [BasinLogLevel.Trace, BasinLogLevel.Debug, BasinLogLevel.Info, BasinLogLevel.Warn, BasinLogLevel.Error],
            lines.Select(l => l.Level));
        Assert.All(lines, l => Assert.Equal(string.Empty, l.Category));
        Assert.All(lines, l => Assert.EndsWith(" (FormatException: Bad.)", l.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void A_message_with_line_breaks_stays_on_one_line()
    {
        var lines = WithSink(BasinLogLevel.Info, sink =>
        {
            Log.Warn(new FormatException("first(1,1) : error : one\n  second(2,1) : error : two\n"), $"config: x did not parse");
            return sink.Lines;
        });

        var line = Assert.Single(lines);
        Assert.Equal(
            "config: x did not parse (FormatException: first(1,1) : error : one second(2,1) : error : two)",
            line.Text);
    }

    [Fact]
    public void A_disabled_level_allocates_nothing()
    {
        var exception = new IOException("Gone.");
        var allocated = WithSink(BasinLogLevel.Error, _ =>
        {
            var count = 3;
            Log.Warn(exception, $"area: {count} failed");
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 16; i++)
            {
                Log.Warn(exception, $"area: {count} failed");
                Log.Debug(exception, $"area: {count} failed");
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        });

        Assert.Equal(0, allocated);
    }
}
