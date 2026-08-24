using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Xunit;

namespace Basin.Tests;

public sealed class OutputPreferencesTests
{
    [Fact]
    public void The_state_setters_mark_their_fields_and_clear_resets_them()
    {
        using var state = new OutputState();
        state.SetRgbRange(OutputRgbRange.Full).SetMaxBitsPerColor(10).SetOverscan(5);

        Assert.Equal(OutputRgbRange.Full, state.RgbRange);
        Assert.Equal(10u, state.MaxBitsPerColor);
        Assert.Equal(5u, state.Overscan);
        Assert.True((state.Fields & OutputStateFields.RgbRange) != 0);
        Assert.True((state.Fields & OutputStateFields.MaxBitsPerColor) != 0);
        Assert.True((state.Fields & OutputStateFields.Overscan) != 0);

        state.Clear();
        Assert.Equal(OutputStateFields.None, state.Fields);
        Assert.Equal(OutputRgbRange.Automatic, state.RgbRange);
        Assert.Equal(0u, state.MaxBitsPerColor);
        Assert.Equal(0u, state.Overscan);
    }

    [Fact]
    public void An_overscan_above_100_is_rejected_at_the_setter()
    {
        using var state = new OutputState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetOverscan(101));
    }

    [Fact]
    public void An_unsupporting_output_rejects_non_neutral_values_and_passes_neutral_ones()
    {
        using var host = new CompositorTestHost();
        var output = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);

        using (var neutral = new OutputState())
        {
            neutral.SetRgbRange(OutputRgbRange.Automatic).SetMaxBitsPerColor(0).SetOverscan(0);
            Assert.True(output.TestCommit(neutral));
        }

        using (var rgb = new OutputState())
        {
            Assert.False(output.TestCommit(rgb.SetRgbRange(OutputRgbRange.Limited)));
        }

        using (var bpc = new OutputState())
        {
            Assert.False(output.TestCommit(bpc.SetMaxBitsPerColor(10)));
        }

        using (var overscan = new OutputState())
        {
            Assert.False(output.TestCommit(overscan.SetOverscan(5)));
        }

        Assert.Equal(OutputConfigurationFeatures.None, ((IOutput)output).Features);
        output.Destroy();
    }

    [Fact]
    public void Supported_masks_the_output_features_to_the_drivable_fields()
    {
        var layout = new OutputLayout();
        var configuration = new LayoutOutputConfiguration(layout);
        var output = new TestPreferenceOutput();

        Assert.Equal(
            OutputConfigurationFeatures.Overscan | OutputConfigurationFeatures.Vrr |
            OutputConfigurationFeatures.RgbRange | OutputConfigurationFeatures.MaxBitsPerColor |
            OutputConfigurationFeatures.CustomModes | OutputConfigurationFeatures.Sharpness |
            OutputConfigurationFeatures.AbmLevel,
            configuration.Supported(output));

        output.Destroy();
    }

    [Fact]
    public void An_applied_preference_reaches_the_output_and_reads_back()
    {
        var layout = new OutputLayout();
        var output = new TestPreferenceOutput();
        layout.Add(output, 0, 0);
        var configuration = new LayoutOutputConfiguration(layout);

        var applied = configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = output,
                Enabled = true,
                Overscan = 10,
                RgbRange = OutputRgbRange.Full,
                MaxBitsPerColor = 10,
                VrrPolicy = OutputVrrPolicy.Always,
            },
        ]);

        Assert.True(applied);
        Assert.Equal(10u, output.CommittedOverscan);
        Assert.Equal(OutputRgbRange.Full, output.CommittedRgbRange);
        Assert.Equal(10u, output.CommittedMaxBpc);
        Assert.True(output.AdaptiveSync);

        Assert.True(configuration.TryRead(output, out var state));
        Assert.Equal(10u, state.Overscan);
        Assert.Equal(OutputRgbRange.Full, state.RgbRange);
        Assert.Equal(10u, state.MaxBitsPerColor);
        Assert.Equal(OutputVrrPolicy.Always, state.VrrPolicy);

        applied = configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = output,
                Enabled = true,
                VrrPolicy = OutputVrrPolicy.Never,
            },
        ]);

        Assert.True(applied);
        Assert.False(output.AdaptiveSync);
        Assert.True(configuration.TryRead(output, out state));
        Assert.Equal(OutputVrrPolicy.Never, state.VrrPolicy);
        Assert.Equal(10u, state.Overscan);

        output.Destroy();
        Assert.False(configuration.TryRead(output, out _));
    }

    [Fact]
    public void Custom_modes_reach_the_output_and_read_back()
    {
        var layout = new OutputLayout();
        var output = new TestPreferenceOutput();
        layout.Add(output, 0, 0);
        var configuration = new LayoutOutputConfiguration(layout);
        var modes = new[] { new OutputMode(1920, 1080, 90_000) };

        Assert.True(configuration.Apply(
        [
            new OutputConfigurationEntry { Output = output, Enabled = true, CustomModes = modes },
        ]));
        Assert.Equal(modes, output.CommittedCustomModes);
        Assert.True(configuration.TryRead(output, out var state));
        Assert.Equal(modes, state.CustomModes);

        output.Destroy();
    }

    [Fact]
    public void A_non_empty_custom_mode_list_on_an_unsupporting_output_fails()
    {
        using var host = new CompositorTestHost();
        var output = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);

        using (var empty = new OutputState())
        {
            Assert.True(output.TestCommit(empty.SetCustomModes([])));
        }

        using (var filled = new OutputState())
        {
            Assert.False(output.TestCommit(filled.SetCustomModes([new OutputMode(800, 600, 60_000)])));
        }

        output.Destroy();
    }

    [Fact]
    public void Sharpness_and_abm_reach_the_output_and_read_back()
    {
        var layout = new OutputLayout();
        var output = new TestPreferenceOutput();
        layout.Add(output, 0, 0);
        var configuration = new LayoutOutputConfiguration(layout);

        Assert.True(configuration.Apply(
        [
            new OutputConfigurationEntry { Output = output, Enabled = true, Sharpness = 4000, AbmLevel = 3 },
        ]));
        Assert.Equal(4000u, output.CommittedSharpness);
        Assert.Equal(3u, output.CommittedAbmLevel);
        Assert.True(configuration.TryRead(output, out var state));
        Assert.Equal(4000u, state.Sharpness);
        Assert.Equal(3u, state.AbmLevel);

        output.Destroy();
    }

    [Fact]
    public void An_accuracy_tradeoff_forces_abm_off()
    {
        var layout = new OutputLayout();
        var output = new TestPreferenceOutput();
        layout.Add(output, 0, 0);
        var configuration = new Basin.Color.ColorOutputConfiguration(new LayoutOutputConfiguration(layout));

        Assert.True(configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = output,
                Enabled = true,
                AbmLevel = 4,
                ColorPowerTradeoff = OutputColorPowerTradeoff.Accuracy,
            },
        ]));
        Assert.Equal(0u, output.CommittedAbmLevel);
        Assert.True(configuration.TryRead(output, out var state));
        Assert.Equal(OutputColorPowerTradeoff.Accuracy, state.ColorPowerTradeoff);

        output.Destroy();
    }

    [Fact]
    public void The_cvt_generator_produces_sane_timings()
    {
        Assert.SkipUnless(Basin.Backend.Drm.Libxcvt.IsAvailable, "libxcvt not present");
        Assert.True(Basin.Backend.Drm.Libxcvt.TryGenerate(1920, 1080, 60_000, reducedBlanking: true, out var mode));
        Assert.Equal(1920u, mode.HDisplay);
        Assert.Equal(1080u, mode.VDisplay);
        Assert.True(mode.HTotal > 1920);
        Assert.True(mode.VTotal > 1080);
        var refresh = mode.DotClock * 1_000_000.0 / (mode.HTotal * (double)mode.VTotal);
        Assert.InRange(refresh, 59_000, 61_000);
    }

    [Fact]
    public void A_non_neutral_preference_on_an_unsupporting_output_fails_the_test()
    {
        using var host = new CompositorTestHost();
        var output = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        var configuration = new LayoutOutputConfiguration(layout);

        Assert.False(configuration.Test(
        [
            new OutputConfigurationEntry { Output = output, Enabled = true, Overscan = 5 },
        ]));
        Assert.False(configuration.TryRead(output, out _));
        output.Destroy();
    }
}
