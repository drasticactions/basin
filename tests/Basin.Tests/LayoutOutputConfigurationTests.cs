using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Xunit;

namespace Basin.Tests;

public sealed class LayoutOutputConfigurationTests
{
    private static readonly OutputMode Old = new(800, 600, 60000);
    private static readonly OutputMode New = new(1024, 768, 60000);

    private static ScriptedOutput Lit(OutputLayout layout, string name, int x)
    {
        var output = new ScriptedOutput(name);
        using var state = new OutputState();
        Assert.True(output.Commit(state.SetEnabled(true).SetMode(Old)));
        layout.Add(output, x, 0);
        return output;
    }

    [Fact]
    public void Apply_returns_false_when_a_commit_fails()
    {
        var layout = new OutputLayout();
        var first = Lit(layout, "A-1", 0);
        var second = Lit(layout, "B-1", 800);
        using var configuration = new LayoutOutputConfiguration(layout);
        var applied = 0;
        configuration.Applied += _ => applied++;
        second.CommitResult = false;

        var ok = configuration.Apply(
        [
            new OutputConfigurationEntry { Output = first, Enabled = true, Mode = New, Scale = 2, Position = new Point(100, 50) },
            new OutputConfigurationEntry { Output = second, Enabled = true, Mode = New },
        ]);

        Assert.False(ok);
        Assert.Equal(0, applied);
        Assert.Equal(Old, first.CurrentMode);
        Assert.Equal(1d, first.Scale);
        Assert.Equal(Old, second.CurrentMode);
        var box = layout.BoxOf(first);
        Assert.Equal((0, 0, 800, 600), (box.X, box.Y, box.Width, box.Height));
        Assert.False(configuration.TryRead(first, out _));
        first.Destroy();
        second.Destroy();
    }

    [Fact]
    public void A_failed_apply_puts_a_disabled_output_back_in_the_layout()
    {
        var layout = new OutputLayout();
        var first = Lit(layout, "A-1", 0);
        var second = Lit(layout, "B-1", 800);
        using var configuration = new LayoutOutputConfiguration(layout);
        second.CommitResult = false;

        var ok = configuration.Apply(
        [
            new OutputConfigurationEntry { Output = first, Enabled = false },
            new OutputConfigurationEntry { Output = second, Enabled = true, Mode = New },
        ]);

        Assert.False(ok);
        Assert.True(first.Enabled);
        Assert.True(layout.Contains(first));
        Assert.Equal(0, layout.BoxOf(first).X);
        first.Destroy();
        second.Destroy();
    }

    [Fact]
    public void A_failed_apply_keeps_a_parked_output_parked()
    {
        var layout = new OutputLayout();
        var first = Lit(layout, "A-1", 300);
        var second = Lit(layout, "B-1", 1100);
        using var configuration = new LayoutOutputConfiguration(layout);
        Assert.True(configuration.Apply([new OutputConfigurationEntry { Output = first, Enabled = false }]));
        Assert.False(layout.Contains(first));
        second.CommitResult = false;

        Assert.False(configuration.Apply(
        [
            new OutputConfigurationEntry { Output = first, Enabled = true },
            new OutputConfigurationEntry { Output = second, Enabled = true, Mode = New },
        ]));
        Assert.False(layout.Contains(first));
        Assert.False(first.Enabled);

        second.CommitResult = true;
        Assert.True(configuration.Apply([new OutputConfigurationEntry { Output = first, Enabled = true }]));
        Assert.Equal(300, layout.BoxOf(first).X);
        first.Destroy();
        second.Destroy();
    }
}
