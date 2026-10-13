using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;

namespace LunaWm.Tests;

public sealed class GreeterConfigTests
{
    private sealed class CollectingSink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) => Lines.Add($"{level}:{message}");
    }

    private static string Shipped([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "samples", "LunaWm", "greeter.toml");

    [Fact]
    public void The_shipped_file_holds_every_default_and_warns_about_nothing()
    {
        var sink = new CollectingSink();
        var previous = BasinLog.Sink;
        var level = BasinLog.Level;
        BasinLog.Sink = sink;
        BasinLog.Level = BasinLogLevel.Trace;
        try
        {
            var shipped = LunaGreeterConfig.Parse(File.ReadAllText(Shipped()), BasinLog.For("test"));
            var empty = LunaGreeterConfig.Parse("", BasinLog.For("test"));

            Assert.DoesNotContain(sink.Lines, line => line.StartsWith("Warn", StringComparison.Ordinal));
            Assert.Equal(empty.Wallpaper, shipped.Wallpaper);
            Assert.Equal(empty.Clock24, shipped.Clock24);
            Assert.Equal(empty.Renderer, shipped.Renderer);
            Assert.Equal(empty.Settings.ListUsers, shipped.Settings.ListUsers);
            Assert.Equal(empty.Settings.Environments, shipped.Settings.Environments);
            Assert.Equal(empty.Settings.X11Wrapper, shipped.Settings.X11Wrapper);
            Assert.Equal(empty.Settings.OnScreen, shipped.Settings.OnScreen);
            Assert.Equal(empty.Settings.OnScreenSize, shipped.Settings.OnScreenSize);
            Assert.Equal(empty.Settings.ScreenReader, shipped.Settings.ScreenReader);
            Assert.Equal(empty.Settings.Orca, shipped.Settings.Orca);
            Assert.Equal(empty.Settings.TextScale, shipped.Settings.TextScale);
            Assert.Equal(empty.Settings.NetworkJoin, shipped.Settings.NetworkJoin);
        }
        finally
        {
            BasinLog.Sink = previous;
            BasinLog.Level = level;
        }
    }

    [Fact]
    public void An_unknown_key_warns_and_a_shared_table_key_does_not()
    {
        var sink = new CollectingSink();
        var previous = BasinLog.Sink;
        var level = BasinLog.Level;
        BasinLog.Sink = sink;
        BasinLog.Level = BasinLogLevel.Trace;
        try
        {
            var config = LunaGreeterConfig.Parse("[look]\nclock = \"24\"\nmystery = 1\n\n[keyboard]\non_screen = \"never\"\n", BasinLog.For("test"));

            Assert.True(config.Clock24);
            Assert.Equal("never", config.Settings.OnScreen);
            Assert.Contains(sink.Lines, line => line.Contains("unknown key 'look.mystery'", StringComparison.Ordinal));
            Assert.DoesNotContain(sink.Lines, line => line.Contains("keyboard.on_screen", StringComparison.Ordinal));
        }
        finally
        {
            BasinLog.Sink = previous;
            BasinLog.Level = level;
        }
    }
}
