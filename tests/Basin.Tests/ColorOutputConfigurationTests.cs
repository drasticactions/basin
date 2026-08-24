using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Color;
using Xunit;

namespace Basin.Tests;

public sealed class ColorOutputConfigurationTests
{
    private static (ColorOutputConfiguration Configuration, OutputLayout Layout) Compose()
    {
        var layout = new OutputLayout();
        return (new ColorOutputConfiguration(new LayoutOutputConfiguration(layout)), layout);
    }

    [Fact]
    public void Supported_adds_the_color_bits_and_brightness_on_every_output()
    {
        var (configuration, _) = Compose();

        var plain = new TestPreferenceOutput();
        Assert.Equal(
            OutputConfigurationFeatures.Overscan | OutputConfigurationFeatures.Vrr |
            OutputConfigurationFeatures.RgbRange | OutputConfigurationFeatures.MaxBitsPerColor |
            OutputConfigurationFeatures.CustomModes | OutputConfigurationFeatures.Sharpness |
            OutputConfigurationFeatures.AbmLevel | OutputConfigurationFeatures.Brightness,
            configuration.Supported(plain));

        var hdr = new TestHdrOutput();
        Assert.Equal(
            OutputConfigurationFeatures.HighDynamicRange | OutputConfigurationFeatures.WideColorGamut |
            OutputConfigurationFeatures.IccProfile | OutputConfigurationFeatures.HdrIccProfile |
            OutputConfigurationFeatures.BuiltInColor | OutputConfigurationFeatures.Brightness,
            configuration.Supported(hdr));

        plain.Destroy();
        hdr.Destroy();
    }

    [Fact]
    public void Enabling_hdr_commits_metadata_and_reads_back()
    {
        var (configuration, layout) = Compose();
        var output = new TestHdrOutput();
        layout.Add(output, 0, 0);

        var applied = configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = output,
                Enabled = true,
                HighDynamicRange = true,
                SdrBrightnessNits = 350,
            },
        ]);

        Assert.True(applied);
        Assert.True(output.HdrFieldSeen);
        Assert.NotNull(output.CommittedHdr);
        Assert.Equal(HdrStaticMetadata.Transfer.Pq, output.CommittedHdr!.Value.Eotf);
        Assert.Equal(600, output.CommittedHdr.Value.MaxMasteringLuminance);

        Assert.True(configuration.TryRead(output, out var state));
        Assert.True(state.HighDynamicRange);
        Assert.Equal(350u, state.SdrBrightnessNits);

        var description = configuration.DescriptionOf(output);
        Assert.Equal(ColorTransferFunction.St2084Pq, description.TransferNamed);
        Assert.Equal(350u, description.Luminances!.Value.Reference);

        configuration.Apply(
        [
            new OutputConfigurationEntry { Output = output, Enabled = true, HighDynamicRange = false },
        ]);
        Assert.Null(output.CommittedHdr);
        Assert.True(configuration.TryRead(output, out state));
        Assert.False(state.HighDynamicRange);

        output.Destroy();
        Assert.True(configuration.TryRead(output, out state));
        Assert.False(state.HighDynamicRange);
    }

    [Fact]
    public void Brightness_and_dimming_ride_a_diagonal_ctm()
    {
        var (configuration, layout) = Compose();
        var output = new TestHdrOutput();
        layout.Add(output, 0, 0);

        configuration.Apply(
        [
            new OutputConfigurationEntry { Output = output, Enabled = true, Brightness = 5000 },
        ]);
        Assert.True(output.CtmFieldSeen);
        Assert.NotNull(output.CommittedCtm);
        Assert.Equal(0.5, output.CommittedCtm![0], 3);
        Assert.Equal(0.5, output.CommittedCtm[4], 3);
        Assert.Equal(0.5, output.CommittedCtm[8], 3);

        configuration.Apply(
        [
            new OutputConfigurationEntry { Output = output, Enabled = true, Dimming = 8000 },
        ]);
        Assert.Equal(0.4, output.CommittedCtm![0], 3);

        configuration.Apply(
        [
            new OutputConfigurationEntry { Output = output, Enabled = true, Brightness = 10000, Dimming = 10000 },
        ]);
        Assert.Null(output.CommittedCtm);

        output.Destroy();
    }

    [Fact]
    public void A_missing_icc_profile_fails_with_a_reason_naming_the_file()
    {
        var (configuration, layout) = Compose();
        var output = new TestHdrOutput();
        layout.Add(output, 0, 0);

        var entries = new OutputConfigurationEntry[]
        {
            new()
            {
                Output = output,
                Enabled = true,
                IccProfilePath = "/nonexistent/profile.icc",
            },
        };

        Assert.False(configuration.Test(entries));
        Assert.Contains("/nonexistent/profile.icc", configuration.LastFailureReason);
        Assert.False(configuration.Apply(entries));
        Assert.True(configuration.TryRead(output, out var state));
        Assert.Equal(string.Empty, state.IccProfilePath);

        output.Destroy();
    }

    [Fact]
    public void A_real_icc_profile_becomes_the_output_description()
    {
        Assert.SkipUnless(Lcms2Support.IsAvailable, "liblcms2 ≥ 2.19 not present");
        var (configuration, layout) = Compose();
        var output = new TestHdrOutput();
        layout.Add(output, 0, 0);

        byte[] icc;
        using (var srgb = Lcms2.IccProfile.CreateSrgb())
        {
            icc = srgb.SaveToArray();
        }

        var path = Path.Combine(Path.GetTempPath(), $"basin-icc-{Environment.ProcessId}.icc");
        File.WriteAllBytes(path, icc);
        try
        {
            var applied = configuration.Apply(
            [
                new OutputConfigurationEntry
                {
                    Output = output,
                    Enabled = true,
                    IccProfilePath = path,
                    ColorProfileSource = OutputColorProfileSource.Icc,
                },
            ]);

            Assert.True(applied);
            var description = configuration.DescriptionOf(output);
            Assert.NotNull(description.IccData);
        }
        finally
        {
            File.Delete(path);
        }

        output.Destroy();
    }

    [Fact]
    public void The_edid_source_uses_the_native_primaries()
    {
        var (configuration, layout) = Compose();
        var output = new TestHdrOutput();
        layout.Add(output, 0, 0);

        configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = output,
                Enabled = true,
                ColorProfileSource = OutputColorProfileSource.Edid,
            },
        ]);

        var description = configuration.DescriptionOf(output);
        Assert.NotNull(description.PrimariesCustom);
        Assert.Equal(680000, description.PrimariesCustom!.Value.Rx);

        output.Destroy();
    }

    [Fact]
    public void Ddc_ci_gates_the_hardware_route_and_the_capability_bit()
    {
        var (configuration, layout) = Compose();
        var output = new TestHdrOutput();
        layout.Add(output, 0, 0);
        var brightness = new DdcBrightness(output);
        configuration.Brightness = brightness;

        Assert.True((configuration.Supported(output) & OutputConfigurationFeatures.DdcCi) != 0);

        configuration.Apply(
        [
            new OutputConfigurationEntry { Output = output, Enabled = true, Brightness = 5000 },
        ]);
        Assert.Equal([50u], brightness.Values);
        Assert.Null(output.CommittedCtm);

        configuration.Apply(
        [
            new OutputConfigurationEntry { Output = output, Enabled = true, DdcCiAllowed = false, Brightness = 2500 },
        ]);
        Assert.Equal([50u], brightness.Values);
        Assert.Equal(0.25, output.CommittedCtm![0], 3);

        Assert.True(configuration.TryRead(output, out var state));
        Assert.False(state.DdcCiAllowed);

        output.Destroy();
    }

    private sealed class DdcBrightness : IOutputBrightness
    {
        private readonly IOutput _output;

        public DdcBrightness(IOutput output) => _output = output;

        public readonly List<uint> Values = [];

        public event Action<IOutput>? BrightnessChanged;

        public bool Supports(IOutput output) => output == _output;

        public uint Max(IOutput output) => 100;

        public bool TryGet(IOutput output, out uint value)
        {
            value = 0;
            return false;
        }

        public bool UsesDdcCi(IOutput output) => output == _output;

        public bool Set(IOutput output, uint value)
        {
            Values.Add(value);
            BrightnessChanged?.Invoke(output);
            return true;
        }
    }

    [Fact]
    public void Edr_raises_the_backlight_and_extends_the_description()
    {
        var (configuration, layout) = Compose();
        var panel = new TestEdidOutput("eDP-1", []);
        layout.Add(panel, 0, 0);
        var backlight = new PanelBrightness(panel);
        configuration.Brightness = backlight;

        Assert.True((configuration.Supported(panel) & OutputConfigurationFeatures.Edr) != 0);

        configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = panel,
                Enabled = true,
                Brightness = 5000,
                EdrPolicy = OutputEdrPolicy.Always,
            },
        ]);
        Assert.Equal(50u, backlight.Value);
        Assert.Equal(1.0, configuration.EdrHeadroomOf(panel), 3);

        configuration.SetEdrDemand(panel, 3.0);
        var expected = 1.04 / 0.54;
        Assert.Equal(expected, configuration.EdrHeadroomOf(panel), 3);
        Assert.Equal(100u, backlight.Value);

        var description = configuration.DescriptionOf(panel);
        Assert.Equal(80u, description.Luminances!.Value.Reference);
        Assert.Equal((uint)Math.Round(80 * expected), description.Luminances.Value.Max);

        configuration.SetEdrDemand(panel, 1.0);
        Assert.Equal(1.0, configuration.EdrHeadroomOf(panel), 3);
        Assert.Equal(50u, backlight.Value);
        Assert.Null(configuration.DescriptionOf(panel).Luminances);

        panel.Destroy();
    }

    [Fact]
    public void Edr_stays_off_without_the_policy_or_off_the_internal_panel()
    {
        var (configuration, layout) = Compose();
        var panel = new TestEdidOutput("eDP-1", []);
        var external = new TestEdidOutput("DP-2", []);
        layout.Add(panel, 0, 0);
        layout.Add(external, 640, 0);
        var backlight = new PanelBrightness(panel);
        configuration.Brightness = backlight;

        Assert.True((configuration.Supported(external) & OutputConfigurationFeatures.Edr) == 0);

        configuration.Apply(
        [
            new OutputConfigurationEntry { Output = panel, Enabled = true, Brightness = 5000 },
        ]);
        configuration.SetEdrDemand(panel, 3.0);
        Assert.Equal(1.0, configuration.EdrHeadroomOf(panel), 3);
        Assert.Equal(50u, backlight.Value);

        panel.Destroy();
        external.Destroy();
    }

    private sealed class PanelBrightness : IOutputBrightness
    {
        private readonly IOutput _output;

        public PanelBrightness(IOutput output) => _output = output;

        public uint Value { get; private set; } = 100;

        public event Action<IOutput>? BrightnessChanged;

        public bool Supports(IOutput output) => output == _output;

        public uint Max(IOutput output) => 100;

        public bool TryGet(IOutput output, out uint value)
        {
            value = Value;
            return true;
        }

        public bool Set(IOutput output, uint value)
        {
            Value = value;
            BrightnessChanged?.Invoke(output);
            return true;
        }
    }

    [Fact]
    public void An_extended_range_sdr_description_encodes_reference_below_full()
    {
        var extended = ImageDescription.Srgb with { Luminances = (0, 160, 80) };
        var characteristics = TransferCharacteristics.From(extended);
        Assert.Equal(80, characteristics.ReferenceLuminance);
        Assert.Equal(160, characteristics.MaxLuminance);
        var signal = characteristics.Encode(80);
        Assert.InRange(signal, 0.6, 0.9);
        Assert.Equal(1.0, characteristics.Encode(160), 3);

        var plain = TransferCharacteristics.From(ImageDescription.Srgb);
        Assert.Equal(1.0, plain.Encode(plain.ReferenceLuminance), 3);
    }

    [Fact]
    public void Hdr_travels_the_kde_wire_and_the_burst_reports_it()
    {
        using var host = new CompositorTestHost();
        var output = new TestHdrOutput();
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        var configuration = new ColorOutputConfiguration(new LayoutOutputConfiguration(layout));
        using var devices = new Basin.Plasma.PlasmaOutputDeviceManager(
            host.Display, layout, new LayoutOutputSet(layout), configuration);
        using var management = new Basin.Plasma.PlasmaOutputManagementManager(host.Display, devices, configuration);

        Basin.Plasma.Protocol.KdeOutputDeviceV2? device = null;
        uint hdrReported = 0;
        uint sdrReported = 0;
        var done = 0;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "kde_output_device_registry_v2")
            {
                var proxy = registry.Bind<Basin.Plasma.Protocol.KdeOutputDeviceRegistryV2>(e.Name, 23);
                proxy.Output += (_, oe) =>
                {
                    device = oe.Output;
                    device.HighDynamicRange += (_, he) => hdrReported = he.HdrEnabled;
                    device.SdrBrightness += (_, se) => sdrReported = se.SdrBrightness;
                    device.Done += (_, _) => done++;
                };
            }
        };
        host.PumpToClient();
        host.PumpUntil(() => done >= 1);
        Assert.Equal(0u, hdrReported);
        Assert.Equal(200u, sdrReported);

        Basin.Plasma.Protocol.KdeOutputManagementV2? managementProxy = null;
        var managementRegistry = host.Client.Display.GetRegistry();
        managementRegistry.Global += (_, e) =>
        {
            if (e.Interface == "kde_output_management_v2")
            {
                managementProxy = managementRegistry.Bind<Basin.Plasma.Protocol.KdeOutputManagementV2>(e.Name, 21);
            }
        };
        host.PumpToClient();
        var config = managementProxy!.CreateConfiguration();
        var applied = false;
        config.Applied += (_, _) => applied = true;
        config.SetHighDynamicRange(device!, 1);
        config.SetSdrBrightness(device!, 400);
        config.Apply();
        host.PumpUntil(() => applied && done >= 2);

        Assert.Equal(1u, hdrReported);
        Assert.Equal(400u, sdrReported);
        Assert.NotNull(output.CommittedHdr);

        output.Destroy();
    }
}
