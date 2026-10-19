using Basin.Config;
using Basin.Diagnostics;
using Basin.Inspector;
using Tomlyn;
using Xunit;

namespace Basin.Tests.ImGui;

public sealed class InspectorSettingsTests
{
    [Fact]
    public void A_missing_table_keeps_the_defaults()
    {
        var defaults = new InspectorSettings { Panels = ["scene"] };
        Assert.Same(defaults, InspectorSettings.Read(null, defaults));
        Assert.True(new InspectorSettings().Enabled);
        Assert.Equal(["outputs"], new InspectorSettings().Panels);
    }

    [Fact]
    public void Every_key_is_read_and_focused_means_no_output_name()
    {
        var settings = Read("[inspector]\nenabled = false\nopen = true\noutput = \"focused\"\npanels = [\"Memory\", \"pose\"]\nfont_size = 18\nrefresh_hz = 30\n", ["pose"], out var warnings);
        Assert.False(settings.Enabled);
        Assert.True(settings.Open);
        Assert.Null(settings.Output);
        Assert.Equal(["Memory", "pose"], settings.Panels);
        Assert.Equal(18, settings.FontSize);
        Assert.Equal(30, settings.RefreshHz);
        Assert.Empty(warnings);

        Assert.Equal("DP-2", Read("[inspector]\noutput = \"DP-2\"\n", null, out _).Output);
    }

    [Fact]
    public void An_unknown_panel_and_values_out_of_range_warn_and_keep_the_defaults()
    {
        var settings = Read("[inspector]\npanels = [\"outputs\", \"nonsense\"]\nfont_size = 2\nrefresh_hz = 1000\n", null, out var warnings);
        Assert.Equal(["outputs"], settings.Panels);
        Assert.Equal(15, settings.FontSize);
        Assert.Equal(10, settings.RefreshHz);
        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, line => line.Contains("nonsense", StringComparison.Ordinal));
    }

    private static InspectorSettings Read(string text, IReadOnlyCollection<string>? extra, out List<string> warnings)
    {
        var sink = new Sink();
        BasinLog.Sink = sink;
        try
        {
            var reader = new TomlReader(Toml.ToModel(text), BasinLog.For("test"));
            var settings = InspectorSettings.Read(reader.Section("inspector"), null, extra);
            warnings = sink.Lines;
            return settings;
        }
        finally
        {
            BasinLog.Sink = null;
        }
    }

    private sealed class Sink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message)
        {
            if (level == BasinLogLevel.Warn)
            {
                Lines.Add(message.ToString());
            }
        }
    }
}
