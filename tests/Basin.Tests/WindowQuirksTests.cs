using Basin.Config;
using Basin.Diagnostics;
using Tomlyn;
using Tomlyn.Model;
using Xunit;

namespace Basin.Tests;

public sealed class WindowQuirksTests
{
    private sealed class CollectingSink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            Lines.Add($"{level}:{message}");
    }

    [Fact]
    public void Every_key_parses_from_a_reader_and_from_a_table()
    {
        var table = (TomlTable)Toml.ToModel("""
            letterbox = "fit"
            scale_filter = "nearest"
            readiness = "immediate"
            force_unmaximize = true
            allow_client_move = true
            emulated_modes = false
            """);
        var expected = new WindowQuirks
        {
            Letterbox = LetterboxQuirk.Fit,
            ScaleFilter = TextureFilter.Nearest,
            Readiness = ReadinessQuirk.Immediate,
            ForceUnmaximize = true,
            AllowClientMove = true,
            EmulatedModes = false,
        };

        var log = BasinLog.For("test");
        Assert.Equal(expected, WindowQuirks.Read(table, log));
        var reader = new TomlReader(table, log);
        Assert.Equal(expected, WindowQuirks.Read(reader));
        Assert.True(WindowQuirks.Read((TomlTable)Toml.ToModel("x = 1"), log).IsEmpty);
    }

    [Fact]
    public void An_unknown_value_warns_and_leaves_the_field_without_an_opinion()
    {
        var sink = new CollectingSink();
        BasinLog.Sink = sink;
        try
        {
            var quirks = WindowQuirks.Read((TomlTable)Toml.ToModel("letterbox = \"stretch\"\nforce_unmaximize = \"yes\"\nx11_scale = \"upscale\""), BasinLog.For("test"));
            Assert.Null(quirks.Letterbox);
            Assert.Null(quirks.ForceUnmaximize);
            Assert.Contains(sink.Lines, line => line.Contains("letterbox \"stretch\" is not fit, center or off", StringComparison.Ordinal));
            Assert.Contains(sink.Lines, line => line.Contains("force_unmaximize \"yes\" is not true or false", StringComparison.Ordinal));
            Assert.Contains(sink.Lines, line => line.Contains("x11_scale is removed", StringComparison.Ordinal));
        }
        finally
        {
            BasinLog.Sink = null;
        }
    }

    [Fact]
    public void Matching_rules_merge_most_specific_first_one_field_at_a_time()
    {
        var general = new WindowRule
        {
            AppIds = ["game"],
            Quirks = new WindowQuirks { Letterbox = LetterboxQuirk.Center, ScaleFilter = TextureFilter.Linear, AllowClientMove = true },
        };
        var specific = new WindowRule
        {
            SteamGames = [570],
            Quirks = new WindowQuirks { Letterbox = LetterboxQuirk.Fit, ScaleFilter = TextureFilter.Nearest },
        };
        var unrelated = new WindowRule
        {
            AppIds = ["other"],
            Quirks = new WindowQuirks { ForceUnmaximize = true },
        };
        var rules = WindowRule.MostSpecificFirst([general, unrelated, specific]);

        var merged = WindowRule.QuirksFor(rules, new WindowFacts { AppId = "game", SteamGame = 570 });

        Assert.Equal(LetterboxQuirk.Fit, merged.Letterbox);
        Assert.Equal(TextureFilter.Nearest, merged.ScaleFilter);
        Assert.True(merged.AllowClientMove);
        Assert.Null(merged.ForceUnmaximize);
        Assert.True(WindowRule.QuirksFor(rules, new WindowFacts { AppId = "nothing" }).IsEmpty);
    }
}
