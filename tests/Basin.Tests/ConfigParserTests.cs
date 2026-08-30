using System.CommandLine;
using Basin.Cli;
using Basin.Config;
using Basin.Diagnostics;
using Tomlyn;
using Tomlyn.Model;
using Xunit;

namespace Basin.Tests;

public sealed class ConfigParserTests
{
    private sealed class CollectingSink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            Lines.Add($"{level}:{message}");
    }

    private static (BasinLogger Log, CollectingSink Sink) Logger()
    {
        var sink = new CollectingSink();
        BasinLog.Sink = sink;
        BasinLog.Level = BasinLogLevel.Trace;
        return (BasinLog.For("test"), sink);
    }

    private static void Restore() => BasinLog.Sink = null;

    [Fact]
    public void Chord_parses_modifiers_and_keysym()
    {
        var (log, _) = Logger();
        try
        {
            Assert.True(HotkeyParser.TryParseChord("Alt+Shift+Left", log, out var keysym, out var modifiers));
            Assert.Equal(Keysym.FromName("Left"), keysym);
            Assert.Equal(Modifiers.Alt | Modifiers.Shift, modifiers);

            Assert.True(HotkeyParser.TryParseChord("SUPER+ctrl+q", log, out _, out var mixed));
            Assert.Equal(Modifiers.Super | Modifiers.Ctrl, mixed);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void Chord_with_an_unknown_modifier_is_dropped_with_a_warning()
    {
        var (log, sink) = Logger();
        try
        {
            Assert.False(HotkeyParser.TryParseChord("Hyper+q", log, out _, out _));
            Assert.Contains(sink.Lines, line => line.Contains("unknown modifier 'Hyper'", StringComparison.Ordinal));
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void Chord_with_an_unknown_keysym_is_dropped_with_a_warning()
    {
        var (log, sink) = Logger();
        try
        {
            Assert.False(HotkeyParser.TryParseChord("Alt+nosuchkey", log, out _, out _));
            Assert.Contains(sink.Lines, line => line.Contains("unknown keysym 'nosuchkey'", StringComparison.Ordinal));
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void A_binding_takes_an_action_a_command_or_an_unbind()
    {
        var (log, _) = Logger();
        try
        {
            var action = HotkeyParser.Parse("Alt+q", "quit", log, name => name == "quit");
            Assert.Equal("quit", action!.Action);
            Assert.Null(action.Command);

            var command = HotkeyParser.Parse("Alt+Return", "foot -e htop", log, static _ => false);
            Assert.Equal(["foot", "-e", "htop"], command!.Command!);

            var table = (TomlTable)Toml.ToModel("[b]\nexec = \"foot\"\n")["b"];
            var exec = HotkeyParser.Parse("Alt+t", table, log, static _ => false);
            Assert.Equal(["foot"], exec!.Command!);

            Assert.True(HotkeyParser.Parse("Alt+u", false, log)!.Unbinds);
            Assert.True(HotkeyParser.Parse("Alt+u", "none", log)!.Unbinds);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void An_unknown_key_warns_and_a_read_one_does_not()
    {
        var (log, sink) = Logger();
        try
        {
            var reader = new TomlReader(Toml.ToModel("known = 1\nmystery = 2\n"), log);
            Assert.Equal(1, reader.Number("known", 0));
            reader.ReportUnknown();

            Assert.Contains(sink.Lines, line => line.Contains("unknown key 'mystery'", StringComparison.Ordinal));
            Assert.DoesNotContain(sink.Lines, line => line.Contains("'known'", StringComparison.Ordinal));
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void An_unknown_choice_keeps_the_default_and_warns()
    {
        var (log, sink) = Logger();
        try
        {
            var reader = new TomlReader(Toml.ToModel("style = \"lozenge\"\n"), log);
            Assert.Equal("beos", reader.Choice("style", "beos", "beos", "flat", "none"));
            Assert.Contains(sink.Lines, line => line.Contains("keeping beos", StringComparison.Ordinal));
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void Rules_order_most_specific_first_and_break_ties_on_file_order()
    {
        var titleOnly = new WindowRule { TitleRegex = new System.Text.RegularExpressions.Regex("a") };
        var appOnly = new WindowRule { AppIds = ["mpv"] };
        var both = new WindowRule
        {
            AppIds = ["mpv"],
            TitleRegex = new System.Text.RegularExpressions.Regex("a"),
        };
        var appOnlySecond = new WindowRule { AppIds = ["mpv"] };

        var ordered = WindowRule.MostSpecificFirst([titleOnly, appOnly, both, appOnlySecond]);

        Assert.Same(both, ordered[0]);
        Assert.Same(appOnly, ordered[1]);
        Assert.Same(appOnlySecond, ordered[2]);
        Assert.Same(titleOnly, ordered[3]);
    }

    [Fact]
    public void A_rule_matches_on_app_id_and_title()
    {
        var rule = new WindowRule
        {
            AppIds = ["mpv"],
            TitleRegex = new System.Text.RegularExpressions.Regex("holiday"),
        };

        Assert.True(rule.MatchesText("mpv", "holiday.mkv"));
        Assert.False(rule.MatchesText("mpv", "work.mkv"));
        Assert.False(rule.MatchesText("firefox", "holiday.mkv"));
        Assert.False(rule.MatchesText("mpv", null));
    }

    [Fact]
    public void A_flag_beats_the_file_which_beats_the_default()
    {
        var command = new RootCommand("test");
        var option = new Option<int>("--outputs") { DefaultValueFactory = _ => 1 };
        command.Options.Add(option);

        var given = command.Parse(["--outputs", "3"]);
        Assert.Equal(3, BasinCommand.Effective(given, option, 2));

        var absent = command.Parse([]);
        Assert.Equal(2, BasinCommand.Effective(absent, option, 2));
        Assert.Equal(1, BasinCommand.Effective(absent, option, 1));
    }

    [Fact]
    public void A_missing_file_reports_a_failure_and_a_parsed_one_does_not()
    {
        var directory = Path.Combine(Path.GetTempPath(), "basin-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var missing = Path.Combine(directory, "absent.toml");
            Assert.Null(TomlConfig.Read(missing, out var absentFailure));
            Assert.NotNull(absentFailure);

            var broken = Path.Combine(directory, "broken.toml");
            File.WriteAllText(broken, "[compositor\n");
            Assert.Null(TomlConfig.Read(broken, out var brokenFailure));
            Assert.NotNull(brokenFailure);

            var good = Path.Combine(directory, "good.toml");
            File.WriteAllText(good, "[compositor]\noutputs = 2\n");
            Assert.NotNull(TomlConfig.Read(good, out var goodFailure));
            Assert.Null(goodFailure);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
