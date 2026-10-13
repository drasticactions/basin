using Basin.Config;
using Basin.Diagnostics;
using Tomlyn;
using Tomlyn.Model;
using Xunit;

namespace Basin.Tests;

public sealed class LaunchRulesTests
{
    private sealed class CollectingSink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            Lines.Add($"{level}:{message}");
    }

    private const string Document = """
        [[launch]]
        exec = "xfig"
        x11 = "low-density"

        [[launch]]
        desktop_id = "leafpad.desktop"
        x11 = "low-density"

        [[launch]]
        desktop_id = "leafpad.desktop"
        exec = "leafpad"
        x11 = "native"

        [[launch]]
        exec = "leafpad"
        x11 = "low-density"
        """;

    [Fact]
    public void The_reader_and_the_table_array_read_the_same_rules()
    {
        var log = BasinLog.For("test");
        var table = (TomlTable)Toml.ToModel(Document);
        var fromReader = LaunchRules.Read(new TomlReader(table, log));
        var fromArray = LaunchRules.Read((TomlTableArray)((TomlTable)Toml.ToModel(Document))["launch"], log);
        Assert.Equal(4, fromReader.Rules.Count);
        Assert.Equal(fromReader.Rules, fromArray.Rules);
    }

    [Fact]
    public void The_most_specific_rule_wins()
    {
        var rules = LaunchRules.Read(new TomlReader((TomlTable)Toml.ToModel(Document), BasinLog.For("test")));
        Assert.Equal(X11Density.Native, rules.For("leafpad.desktop", "leafpad"));
        Assert.Equal(X11Density.Low, rules.For("leafpad.desktop", "/usr/bin/other"));
        Assert.Equal(X11Density.Low, rules.For("leafpad", null));
        Assert.Equal(X11Density.Low, rules.For(null, "leafpad"));
        Assert.Equal(X11Density.Low, rules.For(null, "/usr/bin/xfig"));
        Assert.Null(rules.For("gimp.desktop", "gimp"));
        Assert.Null(rules.For(null, null));
    }

    [Fact]
    public void Equal_specificity_keeps_file_order()
    {
        var rules = LaunchRules.Read(new TomlReader((TomlTable)Toml.ToModel("""
            [[launch]]
            exec = "xterm"
            x11 = "low-density"

            [[launch]]
            exec = "xterm"
            x11 = "native"
            """), BasinLog.For("test")));
        Assert.Equal(X11Density.Low, rules.For(null, "xterm"));
    }

    [Fact]
    public void The_program_of_a_launch_skips_env_assignments_and_the_shell()
    {
        Assert.Equal("leafpad", LaunchRules.ProgramOf("leafpad /tmp/a.txt"));
        Assert.Equal("leafpad", LaunchRules.ProgramOf("env GTK_THEME=Adwaita LANG=C /usr/bin/leafpad"));
        Assert.Equal("leafpad", LaunchRules.ProgramOf("GDK_BACKEND=x11 leafpad"));
        Assert.Equal("my editor", LaunchRules.ProgramOf("\"/opt/x/my editor\" --flag"));
        Assert.Equal("xfig", LaunchRules.ProgramOf(["/bin/sh", "-c", "env A=1 xfig -geometry 10x10"]));
        Assert.Equal("xterm", LaunchRules.ProgramOf(["/usr/bin/xterm", "-e", "sh"]));
        Assert.Equal("sh", LaunchRules.ProgramOf(["/bin/sh", "script.sh"]));
        Assert.Null(LaunchRules.ProgramOf("env A=1"));
        Assert.Null(LaunchRules.ProgramOf([]));

        var rules = new LaunchRules([new LaunchRule { Exec = "leafpad", X11 = X11Density.Low }]);
        Assert.Equal(X11Density.Low, rules.ForCommand(null, "GDK_BACKEND=x11 leafpad"));
        Assert.Equal(X11Density.Low, rules.ForArgv(null, ["/bin/sh", "-c", "leafpad"]));
        Assert.Null(rules.ForArgv(null, ["/bin/sh", "-c", "xterm"]));
    }

    [Fact]
    public void An_unknown_key_or_value_warns_and_a_rule_without_a_match_key_is_dropped()
    {
        var sink = new CollectingSink();
        BasinLog.Sink = sink;
        try
        {
            var log = BasinLog.For("test");
            var table = (TomlTable)Toml.ToModel("""
                [[launch]]
                exec = "xterm"
                x11 = "half"

                [[launch]]
                x11 = "low-density"

                [[launch]]
                exec = "leafpad"
                x11 = "low-density"
                steam = 123
                """);
            var reader = new TomlReader(table, log);
            var rules = LaunchRules.Read(reader);
            reader.ReportUnknown();
            Assert.Equal(2, rules.Rules.Count);
            Assert.Null(rules.For(null, "xterm"));
            Assert.Contains(sink.Lines, line => line.Contains("launch[0].x11 \"half\" is not native or low-density", StringComparison.Ordinal));
            Assert.Contains(sink.Lines, line => line.Contains("launch[1] names neither desktop_id nor exec, dropped", StringComparison.Ordinal));
            Assert.Contains(sink.Lines, line => line.Contains("unknown key 'launch[2].steam'", StringComparison.Ordinal));

            sink.Lines.Clear();
            _ = LaunchRules.Read((TomlTableArray)table["launch"], log);
            Assert.Contains(sink.Lines, line => line.Contains("unknown key 'launch[2].steam'", StringComparison.Ordinal));
        }
        finally
        {
            BasinLog.Sink = null;
        }
    }
}
