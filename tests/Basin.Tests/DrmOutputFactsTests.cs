using Basin.Backend.Drm;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Color;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Scene;
using Tomlyn;
using Xunit;

namespace Basin.Tests;

public sealed class DrmOutputFactsTests
{
    private static readonly DrmTiming DeckOled = new(
        ClockKhz: 1000 * 1312 * 90 / 1000,
        HDisplay: 800, HSyncStart: 840, HSyncEnd: 844, HTotal: 1000,
        VDisplay: 1280, VSyncStart: 1289, VSyncEnd: 1291, VTotal: 1312);

    private static readonly int[] DeckOledSdcFrontPorches =
    [
        1321, 1264, 1209, 1157, 1106,
        1058, 993, 967, 925, 883, 829, 805, 768, 732, 698,
        665, 632, 601, 571, 542, 501, 486, 459, 433, 408,
        383, 360, 337, 314, 292, 271, 250, 230, 210, 191,
        173, 154, 137, 119, 102, 86, 70, 54, 38, 23,
        9,
    ];

    [Fact]
    public void Same_timing_stretches_the_front_porch_as_the_deck_oled_table_does()
    {
        Assert.Equal(90_000, DeckOled.RefreshMilliHz);
        for (var refresh = 45; refresh <= 90; refresh++)
        {
            Assert.True(DrmModeGenerator.TrySameTiming(DeckOled, refresh * 1000, out var mode));
            var slack = refresh is 51 or 55 or 65 ? 20 : 1;
            Assert.InRange(mode.VerticalFrontPorch, DeckOledSdcFrontPorches[refresh - 45] - slack, DeckOledSdcFrontPorches[refresh - 45] + slack);
            Assert.Equal(DeckOled.ClockKhz, mode.ClockKhz);
            Assert.Equal(DeckOled.VSyncEnd - DeckOled.VSyncStart, mode.VSyncEnd - mode.VSyncStart);
            Assert.Equal(DeckOled.VTotal - DeckOled.VSyncEnd, mode.VTotal - mode.VSyncEnd);
            Assert.InRange(mode.RefreshMilliHz, (refresh * 1000) - 100, (refresh * 1000) + 100);
        }

        Assert.False(DrmModeGenerator.TrySameTiming(DeckOled, 120_000, out _));
    }

    [Fact]
    public void Scaled_clock_keeps_the_porches_and_rounds_the_clock_up_like_gamescope()
    {
        Assert.True(DrmModeGenerator.TryScaledClock(DeckOled, 60_000, out var mode));
        Assert.Equal(((1000 * 1312 * 60) + 999) / 1000, mode.ClockKhz);
        Assert.Equal(DeckOled.VTotal, mode.VTotal);
        Assert.Equal(DeckOled.VSyncStart, mode.VSyncStart);
        Assert.InRange(mode.RefreshMilliHz, 59_999, 60_001);
    }

    private static PanelTable Parse(string text) => PanelTable.Parse(new TomlReader(Toml.ToModel(text), BasinLog.For("test")));

    [Fact]
    public void A_panel_entry_reads_every_key()
    {
        var table = Parse("""
            [[panel]]
            vendor = "VLV"
            product = 0x3003
            orientation = "270"
            max_nits = 1000
            max_frame_average_nits = 800
            min_nits = 0
            hdr = true
            output_curve = "gamma22"
            refresh = [45, 60, 90]
            mode_generator = "same_timing"
            max_clock_khz = 140000
            [panel.colorimetry]
            red = [0.6835, 0.3154]
            green = [0.2402, 0.6835]
            blue = [0.1376, 0.0507]
            white = [0.3125, 0.3291]
            """);
        var entry = Assert.Single(table.Entries);
        Assert.Equal("VLV", entry.Vendor);
        Assert.Equal(0x3003, entry.Product);
        Assert.Equal(OutputTransform.Rotate270, entry.Orientation);
        Assert.Equal(1000, entry.MaxNits);
        Assert.Equal(800, entry.MaxFrameAverageNits);
        Assert.Equal(0, entry.MinNits);
        Assert.True(entry.Hdr);
        Assert.Equal(ColorTransferFunction.Gamma22, entry.OutputCurve);
        Assert.Equal([45_000, 60_000, 90_000], entry.RefreshMilliHz);
        Assert.Equal(OutputModeGenerator.SameTiming, entry.ModeGenerator);
        Assert.Equal(140_000, entry.MaxClockKhz);
        Assert.Equal((0.6835, 0.3154, 0.2402, 0.6835, 0.1376, 0.0507, 0.3125, 0.3291), entry.Colorimetry);
    }

    [Fact]
    public void The_most_specific_panel_entry_wins()
    {
        var table = Parse("""
            [[panel]]
            vendor = "VLV"
            max_nits = 100
            [[panel]]
            vendor = "VLV"
            product = 0x3003
            serial = "A1"
            max_nits = 300
            [[panel]]
            vendor = "VLV"
            product = 0x3003
            max_nits = 200
            [[panel]]
            vendor = "XYM"
            max_nits = 400
            """);
        Assert.Equal(300, table.Match(new PanelIdentity("VLV", 0x3003, "A1"))?.MaxNits);
        Assert.Equal(200, table.Match(new PanelIdentity("VLV", 0x3003, "B2"))?.MaxNits);
        Assert.Equal(100, table.Match(new PanelIdentity("vlv", 0x3004, "A1"))?.MaxNits);
        Assert.Null(table.Match(new PanelIdentity("SDC", 0x3003, "A1")));
    }

    [Fact]
    public void A_missing_panel_file_is_an_empty_table()
    {
        var table = PanelTable.Load(BasinLog.For("test"), Path.Combine(Path.GetTempPath(), $"basin-no-panels-{Environment.ProcessId}.toml"));
        Assert.Empty(table.Entries);
    }

    [Fact]
    public void The_panel_identity_comes_from_the_edid_vendor_and_product()
    {
        var edid = new byte[128];
        edid[8] = 0x59;
        edid[9] = 0x96;
        edid[10] = 0x03;
        edid[11] = 0x30;
        var output = new TestEdidOutput("eDP-1", edid);
        var identity = PanelIdentity.Of(output);
        Assert.Equal("VLV", identity.Vendor);
        Assert.Equal(0x3003, identity.Product);
    }

    [Fact]
    public void A_panel_entry_overrides_the_edid_colorimetry_and_the_hdr_peak()
    {
        var output = new TestHdrOutput();
        var configuration = new ColorOutputConfiguration(new LayoutOutputConfiguration(new OutputLayout()));
        configuration.Seed(output, new OutputColorState { HighDynamicRange = true });
        Assert.Equal(600u, configuration.DescriptionOf(output).Luminances!.Value.Max);

        var changed = 0;
        configuration.DescriptionChanged += _ => changed++;
        configuration.SetPanel(output, new PanelEntry { Vendor = "TST", MaxNits = 1000, Hdr = false });
        Assert.Equal(1, changed);
        Assert.Equal(1000, configuration.ColorimetryOf(output)!.Value.MaxLuminance);
        Assert.Equal(1000u, configuration.DescriptionOf(output).Luminances!.Value.Max);
        Assert.Equal(0.68, configuration.ColorimetryOf(output)!.Value.Chromaticities!.Value.Rx);
        Assert.Equal(OutputConfigurationFeatures.None, configuration.Supported(output) & OutputConfigurationFeatures.HighDynamicRange);

        configuration.SetPanel(output, new PanelEntry { Vendor = "TST", OutputCurve = ColorTransferFunction.Srgb });
        configuration.Seed(output, new OutputColorState { Source = OutputColorProfileSource.Srgb });
        Assert.Equal(ColorTransferFunction.Srgb, configuration.DescriptionOf(output).TransferNamed);
    }

    [Fact]
    public void Scanout_stages_the_surfaces_content_type_and_composition_clears_it()
    {
        using var host = new CompositorTestHost();
        var hints = new Basin.Capabilities.Defaults.SurfaceHints();
        using var sceneOutput = new SceneOutput(host.Scene, host.Output) { ScanoutEntryThreshold = 1, SurfaceHints = hints };
        using var swapchain = new Swapchain(new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var window = MappedToplevel.Map(host, host.Client);
        hints.SetContentType(window.ServerSurface, SurfaceContentType.Game);
        var client = DirectScanoutTests.FakeClientBuffer(160, 120);
        var node = new SceneBuffer(host.Scene.Root) { IsOpaque = true, InputSurface = window.ServerSurface };
        node.SetBuffer(client);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True(sceneOutput.IsDirectScanout);
        Assert.True((state.Fields & OutputStateFields.ContentType) != 0);
        Assert.Equal(OutputContentType.Game, state.ContentType);

        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.True((state.Fields & OutputStateFields.ContentType) == 0);

        var cover = new SceneRect(host.Scene.Root, 10, 10, new RenderColor(1f, 0f, 0f, 1f));
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.False(sceneOutput.IsDirectScanout);
        Assert.Equal(OutputContentType.None, state.ContentType);
        Assert.True((state.Fields & OutputStateFields.ContentType) != 0);

        sceneOutput.ContentTypeOverride = OutputContentType.Game;
        cover.SetPosition(1, 1);
        Assert.True(sceneOutput.Commit(host.Renderer, swapchain, state));
        Assert.Equal(OutputContentType.Game, state.ContentType);

        cover.Destroy();
        node.Destroy();
        client.Destroy();
    }

    [Fact]
    public void Video_content_type_maps_to_cinema()
    {
        Assert.Equal(OutputContentType.Cinema, SceneOutput.ContentTypeFor(SurfaceContentType.Video));
        Assert.Equal(OutputContentType.Photo, SceneOutput.ContentTypeFor(SurfaceContentType.Photo));
        Assert.Equal(OutputContentType.None, SceneOutput.ContentTypeFor(SurfaceContentType.None));
    }

    [Fact]
    public void Surface_hints_report_the_composite_default_until_a_client_names_an_encoding()
    {
        using var host = new CompositorTestHost();
        var window = MappedToplevel.Map(host, host.Client);
        var hints = new Basin.Capabilities.Defaults.SurfaceHints();
        ISurfaceHints view = hints;
        Assert.Equal((YuvCoefficients.Bt601, YuvRange.Limited), view.YuvEncodingOf(window.ServerSurface));
        hints.SetYuvEncoding(window.ServerSurface, (YuvCoefficients.Bt709, YuvRange.Full));
        Assert.Equal((YuvCoefficients.Bt709, YuvRange.Full), view.YuvEncodingOf(window.ServerSurface));
        hints.SetYuvEncoding(window.ServerSurface, null);
        Assert.Equal((YuvCoefficients.Bt601, YuvRange.Limited), view.YuvEncodingOf(window.ServerSurface));
    }
}
