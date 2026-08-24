using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Plasma;
using Pixman;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class PlasmaOutputTests
{
    private sealed class DeviceView
    {
        public required Basin.Plasma.Protocol.KdeOutputDeviceV2 Proxy;
        public readonly List<Basin.Plasma.Protocol.KdeOutputDeviceModeV2> Modes = [];
        public readonly Dictionary<string, object> Properties = [];
        public int DoneCount;
        public int CurrentModeCount;
        public bool Removed;
    }

    private sealed class RegistryView
    {
        public required Basin.Plasma.Protocol.KdeOutputDeviceRegistryV2 Proxy;
        public readonly List<DeviceView> Devices = [];
    }

    private static RegistryView BindRegistry(CompositorTestHost host, uint version = 23)
    {
        RegistryView? view = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "kde_output_device_registry_v2")
            {
                var proxy = registry.Bind<Basin.Plasma.Protocol.KdeOutputDeviceRegistryV2>(e.Name, version);
                view = new RegistryView { Proxy = proxy };
                proxy.Output += (_, oe) => view.Devices.Add(WatchDevice(oe.Output));
            }
        };
        host.PumpToClient();
        Assert.NotNull(view);
        return view!;
    }

    private static DeviceView WatchDevice(Basin.Plasma.Protocol.KdeOutputDeviceV2 proxy)
    {
        var device = new DeviceView { Proxy = proxy };
        proxy.Geometry += (_, e) => device.Properties["geometry"] = (e.X, e.Y, e.Make, e.Model, e.Transform);
        proxy.Mode += (_, e) => device.Modes.Add(e.Mode);
        proxy.CurrentMode += (_, _) => device.CurrentModeCount++;
        proxy.Done += (_, _) => device.DoneCount++;
        proxy.Scale += (_, e) => device.Properties["scale"] = e.Factor.ToDouble();
        proxy.Edid += (_, e) => device.Properties["edid"] = e.Raw;
        proxy.Enabled += (_, e) => device.Properties["enabled"] = e.Enabled;
        proxy.Uuid += (_, e) => device.Properties["uuid"] = e.Uuid;
        proxy.SerialNumber += (_, e) => device.Properties["serial_number"] = e.SerialNumber;
        proxy.EisaId += (_, e) => device.Properties["eisa_id"] = e.EisaId;
        proxy.Capabilities += (_, e) => device.Properties["capabilities"] = (uint)e.Flags;
        proxy.Overscan += (_, e) => device.Properties["overscan"] = e.Overscan;
        proxy.VrrPolicyEvent += (_, e) => device.Properties["vrr_policy"] = (uint)e.VrrPolicy;
        proxy.RgbRangeEvent += (_, e) => device.Properties["rgb_range"] = (uint)e.RgbRange;
        proxy.Name += (_, e) => device.Properties["name"] = e.Name;
        proxy.HighDynamicRange += (_, e) => device.Properties["high_dynamic_range"] = e.HdrEnabled;
        proxy.SdrBrightness += (_, e) => device.Properties["sdr_brightness"] = e.SdrBrightness;
        proxy.WideColorGamut += (_, e) => device.Properties["wide_color_gamut"] = e.WcgEnabled;
        proxy.AutoRotatePolicyEvent += (_, e) => device.Properties["auto_rotate_policy"] = (uint)e.Policy;
        proxy.IccProfilePath += (_, e) => device.Properties["icc_profile_path"] = e.ProfilePath;
        proxy.BrightnessOverrides += (_, e) =>
            device.Properties["brightness_overrides"] = (e.MaxPeakBrightness, e.MaxAverageBrightness, e.MinBrightness);
        proxy.SdrGamutWideness += (_, e) => device.Properties["sdr_gamut_wideness"] = e.GamutWideness;
        proxy.ColorProfileSourceEvent += (_, e) => device.Properties["color_profile_source"] = (uint)e.Source;
        proxy.Brightness += (_, e) => device.Properties["brightness"] = e.Brightness;
        proxy.ColorPowerTradeoffEvent += (_, e) => device.Properties["color_power_tradeoff"] = (uint)e.Preference;
        proxy.Dimming += (_, e) => device.Properties["dimming"] = e.Multiplier;
        proxy.ReplicationSource += (_, e) => device.Properties["replication_source"] = e.Source;
        proxy.DdcCiAllowed += (_, e) => device.Properties["ddc_ci_allowed"] = e.Allowed;
        proxy.MaxBitsPerColor += (_, e) => device.Properties["max_bits_per_color"] = e.MaxBpc;
        proxy.MaxBitsPerColorRange += (_, e) => device.Properties["max_bits_per_color_range"] = (e.MinValue, e.MaxValue);
        proxy.AutomaticMaxBitsPerColorLimit += (_, e) =>
            device.Properties["automatic_max_bits_per_color_limit"] = e.MaxBpcLimit;
        proxy.EdrPolicyEvent += (_, e) => device.Properties["edr_policy"] = (uint)e.Policy;
        proxy.Sharpness += (_, e) => device.Properties["sharpness"] = e.Sharpness;
        proxy.Priority += (_, e) => device.Properties["priority"] = e.Priority;
        proxy.AutoBrightness += (_, e) => device.Properties["auto_brightness"] = e.Enabled;
        proxy.Removed += (_, _) => device.Removed = true;
        proxy.HdrIccProfilePath += (_, e) => device.Properties["hdr_icc_profile_path"] = e.ProfilePath;
        proxy.HdrColorProfileSource += (_, e) => device.Properties["hdr_color_profile_source"] = (uint)e.Source;
        proxy.AbmLevel += (_, e) => device.Properties["abm_level"] = e.Level;
        return device;
    }

    private static Basin.Plasma.Protocol.KdeOutputManagementV2 BindManagement(CompositorTestHost host, uint version = 21)
    {
        Basin.Plasma.Protocol.KdeOutputManagementV2? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "kde_output_management_v2")
            {
                proxy = registry.Bind<Basin.Plasma.Protocol.KdeOutputManagementV2>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static WaylandProtocolException ExpectError(CompositorTestHost host)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                host.PumpToClient();
            }
            catch (WaylandProtocolException error)
            {
                return error;
            }
        }

        throw new TimeoutException("no protocol error arrived while pumping");
    }

    private static void AssertClientAlive(CompositorTestHost host)
    {
        var sync = host.Client.Display.Sync();
        var done = false;
        sync.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.True(done);
    }

    private sealed class TestOutputConfiguration : IOutputConfiguration
    {
        public OutputConfigurationFeatures Features;
        public bool Accept = true;
        public string? FailureReason { get; set; }
        public OutputConfigurationEntry? ReadState { get; set; }
        public readonly List<IReadOnlyList<OutputConfigurationEntry>> Applications = [];

        public event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

        public bool Test(IReadOnlyList<OutputConfigurationEntry> entries) => Accept;

        public bool Apply(IReadOnlyList<OutputConfigurationEntry> entries)
        {
            if (!Accept)
            {
                return false;
            }

            Applications.Add(entries);
            Applied?.Invoke(entries);
            return true;
        }

        public OutputConfigurationFeatures Supported(IOutput output) => Features;

        public bool TryRead(IOutput output, out OutputConfigurationEntry state)
        {
            if (ReadState is { } read)
            {
                state = read;
                return true;
            }

            state = default;
            return false;
        }

        public string? LastFailureReason => FailureReason;
    }

    private sealed class TestOutputOrder : IOutputOrder
    {
        public readonly List<IOutput> Ordered = [];

        public event Action? Changed;

        public int Enumerate(Span<IOutput> outputs)
        {
            if (outputs.Length < Ordered.Count)
            {
                return -1;
            }

            for (var i = 0; i < Ordered.Count; i++)
            {
                outputs[i] = Ordered[i];
            }

            return Ordered.Count;
        }

        public void Raise() => Changed?.Invoke();
    }

    private sealed class EdidOutput : OutputBase
    {
        public EdidOutput(string name, string make, string model, string serial)
            : base(name)
        {
            Make = make;
            Model = model;
            Serial = serial;
            using var initial = new OutputState();
            Commit(initial.SetEnabled(true).SetMode(new OutputMode(640, 480, 60_000)));
        }

        protected override bool TestCommitCore(OutputState state) => true;

        protected override bool CommitCore(OutputState state) => true;
    }

    [Fact]
    public void A_registry_bound_below_21_gets_unsupported_version_and_no_output()
    {
        using var host = new CompositorTestHost();
        using var manager = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration: null);

        var view = BindRegistry(host, version: 20);
        var error = ExpectError(host);
        Assert.Equal("kde_output_device_registry_v2", error.InterfaceName);
        Assert.Equal((int)Basin.Plasma.Protocol.KdeOutputDeviceRegistryV2.Error.UnsupportedVersion, (int)error.ErrorCode);
        Assert.Empty(view.Devices);
    }

    [Fact]
    public void A_registry_at_21_announces_one_device_per_output_with_one_done()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 320, 0);
        using var manager = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration: null);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 2 && view.Devices.TrueForAll(d => d.DoneCount >= 1));

        Assert.Equal(2, view.Devices.Count);
        foreach (var device in view.Devices)
        {
            Assert.Equal(1, device.DoneCount);
            Assert.NotEmpty(device.Modes);
        }
    }

    [Fact]
    public void A_hotplug_announces_on_every_bound_registry()
    {
        using var host = new CompositorTestHost();
        using var manager = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration: null);

        var first = BindRegistry(host);
        var secondRegistry = BindRegistry(host);
        host.PumpUntil(() => first.Devices.Count == 1 && secondRegistry.Devices.Count == 1);

        var plugged = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(plugged, 320, 0);
        host.PumpUntil(() => first.Devices.Count == 2 && secondRegistry.Devices.Count == 2);

        Assert.Equal(2, first.Devices.Count);
        Assert.Equal(2, secondRegistry.Devices.Count);
    }

    [Fact]
    public void An_unplug_sends_removed_and_the_device_survives_until_release()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 320, 0);
        using var manager = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration: null);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 2 && view.Devices.TrueForAll(d => d.DoneCount >= 1));
        var device = view.Devices.Find(d => (string)d.Properties["name"] == second.Name);
        Assert.NotNull(device);

        host.Layout.Remove(second);
        second.Destroy();
        host.PumpUntil(() => device!.Removed);

        device!.Proxy.Release();
        host.PumpToServer();
        AssertClientAlive(host);
    }

    [Fact]
    public void Current_mode_is_absent_for_a_disabled_output_and_present_for_an_enabled_one()
    {
        using var host = new CompositorTestHost();
        using var manager = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration: null);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 1 && view.Devices[0].DoneCount >= 1);
        var device = view.Devices[0];
        Assert.Equal(1, device.CurrentModeCount);
        Assert.Equal(1, (int)device.Properties["enabled"]);

        using (var disable = new OutputState())
        {
            host.Output.Commit(disable.SetEnabled(false));
        }

        host.PumpUntil(() => device.DoneCount >= 2);
        Assert.Equal(0, (int)device.Properties["enabled"]);
        Assert.Equal(1, device.CurrentModeCount);

        using (var enable = new OutputState())
        {
            host.Output.Commit(enable.SetEnabled(true));
        }

        host.PumpUntil(() => device.DoneCount >= 3);
    }

    [Fact]
    public void A_repaint_sends_no_state_and_a_reconfiguration_does()
    {
        using var host = new CompositorTestHost();
        using var manager = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration: null);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 1 && view.Devices[0].DoneCount >= 1);
        var device = view.Devices[0];
        Assert.Equal(1, device.DoneCount);

        using var damage = new PixmanRegion32();
        damage.UnionRect(damage, 0, 0, 640, 480);
        for (var frame = 0; frame < 10; frame++)
        {
            using var repaint = new OutputState();
            Assert.True(host.Output.Commit(repaint.SetDamage(damage)));
        }

        host.PumpToClient();
        Assert.Equal(1, device.DoneCount);

        using (var rescale = new OutputState())
        {
            Assert.True(host.Output.Commit(rescale.SetScale(2)));
        }

        host.PumpUntil(() => device.DoneCount >= 2);
        Assert.Equal(2.0, (double)device.Properties["scale"]);
    }

    [Fact]
    public void The_uuid_is_identical_across_two_compositor_lifetimes()
    {
        var uuids = new List<string>();
        for (var run = 0; run < 2; run++)
        {
            using var host = new CompositorTestHost();
            var output = new EdidOutput("DP-9", "ACME", "Panel 27", "S1234567");
            var layout = new OutputLayout();
            layout.Add(output, 0, 0);
            using (var manager = new PlasmaOutputDeviceManager(
                host.Display, layout, new LayoutOutputSet(layout), configuration: null))
            {
                var view = BindRegistry(host);
                host.PumpUntil(() => view.Devices.Count == 1 && view.Devices[0].DoneCount >= 1);
                uuids.Add((string)view.Devices[0].Properties["uuid"]);
            }

            output.Destroy();
        }

        Assert.NotEmpty(uuids[0]);
        Assert.Equal(uuids[0], uuids[1]);
    }

    [Fact]
    public void Every_unbacked_property_answers_the_neutral_value()
    {
        using var host = new CompositorTestHost();
        using var manager = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration: null);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 1 && view.Devices[0].DoneCount >= 1);
        var p = view.Devices[0].Properties;

        Assert.Equal(host.Output.Name, (string)p["name"]);
        Assert.Equal(1, (int)p["enabled"]);
        Assert.Equal(1.0, (double)p["scale"]);
        Assert.Equal(string.Empty, (string)p["edid"]);
        Assert.Equal(string.Empty, (string)p["serial_number"]);
        Assert.Equal(string.Empty, (string)p["eisa_id"]);
        Assert.NotEmpty((string)p["uuid"]);
        Assert.Equal(0u, (uint)p["capabilities"]);
        Assert.Equal(0u, (uint)p["overscan"]);
        Assert.Equal(2u, (uint)p["vrr_policy"]);
        Assert.Equal(0u, (uint)p["rgb_range"]);
        Assert.Equal(0u, (uint)p["high_dynamic_range"]);
        Assert.Equal(200u, (uint)p["sdr_brightness"]);
        Assert.Equal(0u, (uint)p["wide_color_gamut"]);
        Assert.Equal(0u, (uint)p["auto_rotate_policy"]);
        Assert.Equal(string.Empty, (string)p["icc_profile_path"]);
        Assert.Equal((-1, -1, -1), ((int, int, int))p["brightness_overrides"]);
        Assert.Equal(0u, (uint)p["sdr_gamut_wideness"]);
        Assert.Equal(0u, (uint)p["color_profile_source"]);
        Assert.Equal(10000u, (uint)p["brightness"]);
        Assert.Equal(0u, (uint)p["color_power_tradeoff"]);
        Assert.Equal(10000u, (uint)p["dimming"]);
        Assert.Equal(string.Empty, (string)p["replication_source"]);
        Assert.Equal(0u, (uint)p["ddc_ci_allowed"]);
        Assert.Equal(8u, (uint)p["max_bits_per_color"]);
        Assert.Equal((8u, 8u), ((uint, uint))p["max_bits_per_color_range"]);
        Assert.Equal(0u, (uint)p["automatic_max_bits_per_color_limit"]);
        Assert.Equal(0u, (uint)p["edr_policy"]);
        Assert.Equal(0u, (uint)p["sharpness"]);
        Assert.Equal(0u, (uint)p["priority"]);
        Assert.Equal(0u, (uint)p["auto_brightness"]);
        Assert.Equal(string.Empty, (string)p["hdr_icc_profile_path"]);
        Assert.Equal(0u, (uint)p["hdr_color_profile_source"]);
        Assert.Equal(0u, (uint)p["abm_level"]);
        Assert.False(p.ContainsKey("brightness_metadata"));
    }

    [Fact]
    public void A_configuration_with_mode_and_position_applies_once()
    {
        using var host = new CompositorTestHost();
        var configuration = new TestOutputConfiguration();
        using var devices = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration);
        using var management = new PlasmaOutputManagementManager(host.Display, devices, configuration);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 1 && view.Devices[0].Modes.Count >= 1);
        var device = view.Devices[0];

        var proxy = BindManagement(host);
        var config = proxy.CreateConfiguration();
        var applied = false;
        config.Applied += (_, _) => applied = true;
        config.Position(device.Proxy, 10, 20);
        config.Mode(device.Proxy, device.Modes[0]);
        config.Apply();
        host.PumpUntil(() => applied);

        var entries = Assert.Single(configuration.Applications);
        var entry = Assert.Single(entries);
        Assert.Equal(host.Output, entry.Output);
        Assert.True(entry.Enabled);
        Assert.Equal(new Point(10, 20), entry.Position);
        Assert.Equal(host.Output.CurrentMode, entry.Mode);
    }

    [Fact]
    public void A_second_apply_raises_already_applied()
    {
        using var host = new CompositorTestHost();
        var configuration = new TestOutputConfiguration();
        using var devices = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration);
        using var management = new PlasmaOutputManagementManager(host.Display, devices, configuration);

        var proxy = BindManagement(host);
        var config = proxy.CreateConfiguration();
        var applied = false;
        config.Applied += (_, _) => applied = true;
        config.Apply();
        host.PumpUntil(() => applied);

        config.Apply();
        var error = ExpectError(host);
        Assert.Equal("kde_output_configuration_v2", error.InterfaceName);
        Assert.Equal(
            (int)Basin.Plasma.Protocol.KdeOutputConfigurationV2.Error.AlreadyApplied, (int)error.ErrorCode);
    }

    [Fact]
    public void An_unsupported_field_fails_with_a_reason_naming_it()
    {
        using var host = new CompositorTestHost();
        var configuration = new TestOutputConfiguration();
        using var devices = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration);
        using var management = new PlasmaOutputManagementManager(host.Display, devices, configuration);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 1);

        var proxy = BindManagement(host);
        var config = proxy.CreateConfiguration();
        string? reason = null;
        var failed = false;
        config.FailureReason += (_, e) => reason = e.Reason;
        config.Failed += (_, _) => failed = true;
        config.SetSharpness(view.Devices[0].Proxy, 5000);
        config.Apply();
        host.PumpUntil(() => failed);

        Assert.Equal("sharpness is not supported on this output", reason);
        Assert.Empty(configuration.Applications);
        AssertClientAlive(host);
    }

    [Fact]
    public void Custom_modes_reach_the_entry_and_a_mode_without_a_refresh_rate_is_an_error()
    {
        using var host = new CompositorTestHost();
        var configuration = new TestOutputConfiguration { Features = OutputConfigurationFeatures.CustomModes };
        using var devices = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration);
        using var management = new PlasmaOutputManagementManager(host.Display, devices, configuration);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 1);

        var proxy = BindManagement(host);
        var modes = proxy.CreateModeList();
        modes.SetResolution(1920, 1080);
        modes.SetRefreshRate(90_000);
        modes.AddMode();
        var config = proxy.CreateConfiguration();
        var applied = false;
        config.Applied += (_, _) => applied = true;
        config.SetCustomModes(view.Devices[0].Proxy, modes);
        config.Apply();
        host.PumpUntil(() => applied);

        var entries = Assert.Single(configuration.Applications);
        var entry = Assert.Single(entries);
        var custom = Assert.Single(entry.CustomModes!);
        Assert.Equal(new OutputMode(1920, 1080, 90_000), custom);

        var incomplete = proxy.CreateModeList();
        incomplete.SetResolution(800, 600);
        incomplete.AddMode();
        var error = ExpectError(host);
        Assert.Equal("kde_mode_list_v2", error.InterfaceName);
        Assert.Equal(
            (int)Basin.Plasma.Protocol.KdeModeListV2.Error.MissingParameters, (int)error.ErrorCode);
    }

    [Fact]
    public void A_connector_preference_applies_through_the_default_driver_and_reads_back()
    {
        using var host = new CompositorTestHost();
        var output = new TestPreferenceOutput();
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        var configuration = new LayoutOutputConfiguration(layout);
        using var devices = new PlasmaOutputDeviceManager(
            host.Display, layout, new LayoutOutputSet(layout), configuration);
        using var management = new PlasmaOutputManagementManager(host.Display, devices, configuration);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 1 && view.Devices[0].DoneCount >= 1);
        var device = view.Devices[0];
        Assert.Equal(
            (uint)(OutputConfigurationFeatures.Overscan | OutputConfigurationFeatures.Vrr |
                   OutputConfigurationFeatures.RgbRange | OutputConfigurationFeatures.MaxBitsPerColor |
                   OutputConfigurationFeatures.CustomModes | OutputConfigurationFeatures.Sharpness |
                   OutputConfigurationFeatures.AbmLevel),
            (uint)device.Properties["capabilities"]);
        Assert.Equal(0u, (uint)device.Properties["max_bits_per_color"]);

        var proxy = BindManagement(host);
        var config = proxy.CreateConfiguration();
        var applied = false;
        config.Applied += (_, _) => applied = true;
        config.Overscan(device.Proxy, 10);
        config.SetRgbRange(device.Proxy, Basin.Plasma.Protocol.KdeOutputConfigurationV2.RgbRange.Full);
        config.SetVrrPolicy(device.Proxy, Basin.Plasma.Protocol.KdeOutputConfigurationV2.VrrPolicy.Always);
        config.Apply();
        host.PumpUntil(() => applied && device.DoneCount >= 2);

        Assert.Equal(10u, output.CommittedOverscan);
        Assert.Equal(OutputRgbRange.Full, output.CommittedRgbRange);
        Assert.True(output.AdaptiveSync);
        Assert.Equal(10u, (uint)device.Properties["overscan"]);
        Assert.Equal(1u, (uint)device.Properties["rgb_range"]);
        Assert.Equal(1u, (uint)device.Properties["vrr_policy"]);

        output.Destroy();
    }

    [Fact]
    public void A_replication_source_parks_the_replica_and_reads_back()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 320, 0);
        var configuration = new LayoutOutputConfiguration(host.Layout);
        using var devices = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration);
        using var management = new PlasmaOutputManagementManager(host.Display, devices, configuration);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 2 && view.Devices.TrueForAll(d => d.DoneCount >= 1));
        var sourceDevice = view.Devices.Find(d => (string)d.Properties["name"] == host.Output.Name)!;
        var replicaDevice = view.Devices.Find(d => (string)d.Properties["name"] == second.Name)!;
        var sourceUuid = (string)sourceDevice.Properties["uuid"];

        var proxy = BindManagement(host);
        var config = proxy.CreateConfiguration();
        var applied = false;
        config.Applied += (_, _) => applied = true;
        config.SetReplicationSource(replicaDevice.Proxy, sourceUuid);
        config.Apply();
        host.PumpUntil(() => applied && replicaDevice.DoneCount >= 2);

        Assert.False(host.Layout.Contains(second));
        Assert.True(second.Enabled);
        Assert.Equal(1, (int)replicaDevice.Properties["enabled"]);
        Assert.Equal(sourceUuid, (string)replicaDevice.Properties["replication_source"]);

        var restore = proxy.CreateConfiguration();
        var restored = false;
        restore.Applied += (_, _) => restored = true;
        restore.SetReplicationSource(replicaDevice.Proxy, string.Empty);
        restore.Apply();
        host.PumpUntil(() => restored && replicaDevice.DoneCount >= 3);

        Assert.True(host.Layout.Contains(second));
        Assert.Equal(string.Empty, (string)replicaDevice.Properties["replication_source"]);
        second.Destroy();
    }

    [Fact]
    public void The_priority_follows_the_output_order_with_unordered_outputs_after_it()
    {
        using var host = new CompositorTestHost();
        var second = host.Backend.CreateOutput(new OutputMode(160, 120, 60_000), manualFrameClock: true);
        host.Layout.Add(second, 320, 0);
        var order = new TestOutputOrder();
        order.Ordered.Add(second);
        using var manager = new PlasmaOutputDeviceManager(
            host.Display, host.Layout, new LayoutOutputSet(host.Layout), configuration: null, order);

        var view = BindRegistry(host);
        host.PumpUntil(() => view.Devices.Count == 2 && view.Devices.TrueForAll(d => d.DoneCount >= 1));

        uint PriorityOf(IOutput output)
        {
            var device = view.Devices.Find(d => (string)d.Properties["name"] == output.Name);
            Assert.NotNull(device);
            return (uint)device!.Properties["priority"];
        }

        Assert.Equal(0u, PriorityOf(second));
        Assert.Equal(1u, PriorityOf(host.Output));

        order.Ordered.Clear();
        order.Ordered.Add(host.Output);
        order.Raise();
        host.PumpUntil(() => view.Devices.TrueForAll(d => d.DoneCount >= 2));

        Assert.Equal(0u, PriorityOf(host.Output));
        Assert.Equal(1u, PriorityOf(second));
    }
}
