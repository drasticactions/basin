using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class PlasmaOutputDeviceManager : IDisposable
{
    public const int Version = 23;

    private const OutputStateFields Reported =
        OutputStateFields.Enabled | OutputStateFields.Mode | OutputStateFields.Scale |
        OutputStateFields.Transform | OutputStateFields.Hdr | OutputStateFields.RgbRange |
        OutputStateFields.MaxBitsPerColor | OutputStateFields.Overscan |
        OutputStateFields.CustomModes | OutputStateFields.Sharpness | OutputStateFields.AbmLevel;

    private readonly OutputLayout _layout;
    private readonly IOutputSet? _outputs;
    private readonly IOutputConfiguration? _configuration;
    private readonly IOutputOrder? _order;
    private readonly WlGlobal _global;
    private IOutput[] _orderScratch = new IOutput[8];
    private readonly List<RegistryState> _registries = [];
    private readonly Dictionary<IOutput, Action<OutputStateFields>> _watched = [];

    private sealed class RegistryState
    {
        public required KdeOutputDeviceRegistryV2Resource Resource;
        public required List<DeviceState> Devices;
    }

    private sealed class DeviceState
    {
        public required IOutput Output;
        public required KdeOutputDeviceV2Resource Resource;
        public required List<ModeState> Modes;
    }

    private sealed class ModeState
    {
        public required KdeOutputDeviceModeV2Resource Resource;
        public required OutputMode Mode;
        public required bool Custom;
    }

    public PlasmaOutputDeviceManager(
        WlServerDisplay display,
        OutputLayout layout,
        IOutputSet? outputs,
        IOutputConfiguration? configuration,
        IOutputOrder? order = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _outputs = outputs;
        _configuration = configuration;
        _order = order;
        _global = display.CreateGlobal(KdeOutputDeviceRegistryV2.Interface, Version, OnBind);
        layout.Changed += RefreshAll;
        if (outputs is not null)
        {
            outputs.Changed += Sync;
        }

        if (configuration is not null)
        {
            configuration.Applied += OnApplied;
        }

        if (order is not null)
        {
            order.Changed += RefreshAll;
        }

        foreach (var output in Outputs)
        {
            Watch(output);
        }
    }

    public void Dispose()
    {
        _layout.Changed -= RefreshAll;
        if (_outputs is not null)
        {
            _outputs.Changed -= Sync;
        }

        if (_configuration is not null)
        {
            _configuration.Applied -= OnApplied;
        }

        if (_order is not null)
        {
            _order.Changed -= RefreshAll;
        }

        foreach (var (output, handler) in _watched)
        {
            output.Committed -= handler;
        }

        _watched.Clear();
        _global.Dispose();
    }

    internal bool TryResolveDevice(KdeOutputDeviceV2Resource resource, out IOutput output)
    {
        foreach (var registry in _registries)
        {
            foreach (var device in registry.Devices)
            {
                if (device.Resource == resource)
                {
                    output = device.Output;
                    return true;
                }
            }
        }

        output = null!;
        return false;
    }

    internal bool TryResolveMode(
        KdeOutputDeviceV2Resource device, KdeOutputDeviceModeV2Resource resource, out OutputMode mode)
    {
        foreach (var registry in _registries)
        {
            foreach (var state in registry.Devices)
            {
                if (state.Resource != device)
                {
                    continue;
                }

                foreach (var candidate in state.Modes)
                {
                    if (candidate.Resource == resource)
                    {
                        mode = candidate.Mode;
                        return true;
                    }
                }
            }
        }

        mode = default;
        return false;
    }

    internal static bool IsEnabled(IOutput output) => output.Enabled;

    private IReadOnlyList<IOutput> Outputs => _outputs?.Outputs ?? [];

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new KdeOutputDeviceRegistryV2Resource(client, version, id);
        if (version < 21)
        {
            resource.PostError(
                (uint)KdeOutputDeviceRegistryV2.Error.UnsupportedVersion,
                $"kde_output_device_registry_v2 needs version 21, version {version} was bound");
            return;
        }

        var registry = new RegistryState { Resource = resource, Devices = [] };
        _registries.Add(registry);
        resource.Destroyed += (_, _) => _registries.Remove(registry);
        resource.Stop += (_, _) =>
        {
            resource.SendFinished();
            resource.Destroy();
        };

        foreach (var output in Outputs)
        {
            Announce(registry, output);
        }
    }

    private void Announce(RegistryState registry, IOutput output)
    {
        Watch(output);
        var resource = new KdeOutputDeviceV2Resource(registry.Resource.Client, registry.Resource.Version, 0);
        registry.Resource.SendOutput(resource);
        var device = new DeviceState { Output = output, Resource = resource, Modes = [] };
        registry.Devices.Add(device);
        SendState(device);
    }

    private void Sync()
    {
        foreach (var registry in _registries)
        {
            if (registry.Resource.IsDestroyed)
            {
                continue;
            }

            for (var i = registry.Devices.Count - 1; i >= 0; i--)
            {
                var device = registry.Devices[i];
                if (!Outputs.Contains(device.Output))
                {
                    device.Resource.SendRemoved();
                    registry.Devices.RemoveAt(i);
                }
            }

            foreach (var output in Outputs)
            {
                if (!registry.Devices.Exists(device => device.Output == output))
                {
                    Announce(registry, output);
                }
            }
        }

        var stale = _watched.Keys.Where(output => !Outputs.Contains(output)).ToList();
        foreach (var output in stale)
        {
            output.Committed -= _watched[output];
            _watched.Remove(output);
        }
    }

    private void Watch(IOutput output)
    {
        if (_watched.ContainsKey(output))
        {
            return;
        }

        void OnCommitted(OutputStateFields fields)
        {
            if ((fields & Reported) != 0)
            {
                RefreshOutput(output);
            }
        }

        _watched[output] = OnCommitted;
        output.Committed += OnCommitted;
    }

    private void OnApplied(IReadOnlyList<OutputConfigurationEntry> entries) => RefreshAll();

    private void RefreshAll()
    {
        foreach (var registry in _registries)
        {
            if (registry.Resource.IsDestroyed)
            {
                continue;
            }

            foreach (var device in registry.Devices)
            {
                SendState(device);
            }
        }
    }

    private void RefreshOutput(IOutput output)
    {
        foreach (var registry in _registries)
        {
            if (registry.Resource.IsDestroyed)
            {
                continue;
            }

            foreach (var device in registry.Devices)
            {
                if (device.Output == output)
                {
                    SendState(device);
                }
            }
        }
    }

    private void SendState(DeviceState device)
    {
        var output = device.Output;
        var resource = device.Resource;
        if (resource.IsDestroyed)
        {
            return;
        }

        var state = ReadState(output);
        var enabled = output.Enabled;
        var x = 0;
        var y = 0;
        if (_layout.Contains(output))
        {
            var box = _layout.BoxOf(output);
            x = box.X;
            y = box.Y;
        }

        var (physicalWidth, physicalHeight) = output.PhysicalSize;
        resource.SendGeometry(x, y, physicalWidth, physicalHeight, 0, output.Make, output.Model, (int)output.Transform);
        SyncModes(device, state, enabled);
        resource.SendScale(WlFixed.FromDouble(output.Scale));
        resource.SendEdid(EdidBase64(output));
        resource.SendEnabled(enabled ? 1 : 0);
        resource.SendUuid(PlasmaOutputUuid.For(output));
        resource.SendSerialNumber(output.Serial);
        resource.SendEisaId(EisaId(output));
        resource.SendCapabilities(
            (KdeOutputDeviceV2.Capability)(_configuration?.Supported(output) ?? OutputConfigurationFeatures.None));
        resource.SendOverscan(state.Overscan ?? 0);
        resource.SendVrrPolicy((KdeOutputDeviceV2.VrrPolicy)(state.VrrPolicy ?? OutputVrrPolicy.Automatic));
        resource.SendRgbRange((KdeOutputDeviceV2.RgbRange)(state.RgbRange ?? OutputRgbRange.Automatic));
        resource.SendName(output.Name);
        resource.SendHighDynamicRange(state.HighDynamicRange == true ? 1u : 0u);
        resource.SendSdrBrightness(state.SdrBrightnessNits ?? 200);
        resource.SendWideColorGamut(state.WideColorGamut == true ? 1u : 0u);
        resource.SendAutoRotatePolicy(
            (KdeOutputDeviceV2.AutoRotatePolicy)(state.AutoRotate ?? OutputAutoRotatePolicy.Never));
        resource.SendIccProfilePath(state.IccProfilePath ?? string.Empty);
        if (output is Backend.Drm.DrmOutput drm && drm.Edid.MaxLuminance > 0)
        {
            resource.SendBrightnessMetadata(
                (uint)drm.Edid.MaxLuminance,
                (uint)drm.Edid.MaxFrameAverageLuminance,
                (uint)(drm.Edid.MinLuminance * 10000));
        }

        var overrides = state.BrightnessOverrides ?? OutputBrightnessOverrides.None;
        resource.SendBrightnessOverrides(
            overrides.MaxPeakBrightness, overrides.MaxFrameAverageBrightness, overrides.MinBrightness);
        resource.SendSdrGamutWideness(state.SdrGamutWideness ?? 0);
        resource.SendColorProfileSource(
            (KdeOutputDeviceV2.ColorProfileSource)(state.ColorProfileSource ?? OutputColorProfileSource.Srgb));
        resource.SendBrightness(state.Brightness ?? 10000);
        resource.SendColorPowerTradeoff(
            (KdeOutputDeviceV2.ColorPowerTradeoff)(state.ColorPowerTradeoff ?? OutputColorPowerTradeoff.Efficiency));
        resource.SendDimming(state.Dimming ?? 10000);
        resource.SendReplicationSource(state.ReplicationSourceUuid ?? string.Empty);
        resource.SendDdcCiAllowed(state.DdcCiAllowed == true ? 1u : 0u);
        var (bpcMin, bpcMax) = (output as Backend.Drm.DrmOutput)?.MaxBitsPerColorRange ?? (8u, 8u);
        var supportsMaxBpc =
            ((_configuration?.Supported(output) ?? OutputConfigurationFeatures.None) &
             OutputConfigurationFeatures.MaxBitsPerColor) != 0;
        resource.SendMaxBitsPerColor(state.MaxBitsPerColor ?? (supportsMaxBpc ? 0u : 8u));
        resource.SendMaxBitsPerColorRange(bpcMin, bpcMax);
        resource.SendAutomaticMaxBitsPerColorLimit(0);
        resource.SendEdrPolicy((KdeOutputDeviceV2.EdrPolicy)(state.EdrPolicy ?? OutputEdrPolicy.Never));
        resource.SendSharpness(state.Sharpness ?? 0);
        resource.SendPriority(PriorityOf(output));
        resource.SendAutoBrightness(state.AutoBrightness == true ? 1u : 0u);
        if (resource.Version >= 22)
        {
            resource.SendHdrIccProfilePath(state.HdrIccProfilePath ?? string.Empty);
            resource.SendHdrColorProfileSource(
                (KdeOutputDeviceV2.ColorProfileSource)(state.HdrColorProfileSource ?? OutputColorProfileSource.Srgb));
        }

        if (resource.Version >= 23)
        {
            resource.SendAbmLevel(state.AbmLevel ?? 0);
        }

        resource.SendDone();
    }

    private OutputConfigurationEntry ReadState(IOutput output)
    {
        if (_configuration is { } configuration && configuration.TryRead(output, out var state))
        {
            return state;
        }

        return default;
    }

    private void SyncModes(DeviceState device, in OutputConfigurationEntry state, bool enabled)
    {
        var output = device.Output;
        IReadOnlyList<OutputMode> baseModes = (output as Backend.Drm.DrmOutput)?.Modes ?? [output.CurrentMode];
        var preferred = (output as Backend.Drm.DrmOutput)?.PreferredMode ?? output.CurrentMode;
        IReadOnlyList<OutputMode> custom = state.CustomModes ?? [];

        for (var i = device.Modes.Count - 1; i >= 0; i--)
        {
            var mode = device.Modes[i];
            if (mode.Custom && !custom.Contains(mode.Mode))
            {
                mode.Resource.SendRemoved();
                mode.Resource.Destroy();
                device.Modes.RemoveAt(i);
            }
        }

        foreach (var mode in baseModes)
        {
            EnsureMode(device, mode, custom: false, preferred: mode == preferred);
        }

        foreach (var mode in custom)
        {
            EnsureMode(device, mode, custom: true, preferred: false);
        }

        if (!device.Modes.Exists(m => m.Mode == output.CurrentMode))
        {
            EnsureMode(device, output.CurrentMode, custom: false, preferred: false);
        }

        if (enabled)
        {
            var current = device.Modes.Find(m => m.Mode == output.CurrentMode);
            if (current is not null)
            {
                device.Resource.SendCurrentMode(current.Resource);
            }
        }
    }

    private static void EnsureMode(DeviceState device, OutputMode mode, bool custom, bool preferred)
    {
        if (device.Modes.Exists(m => m.Mode == mode))
        {
            return;
        }

        var resource = new KdeOutputDeviceModeV2Resource(device.Resource.Client, device.Resource.Version, 0);
        device.Resource.SendMode(resource);
        resource.SendSize(mode.Width, mode.Height);
        if (mode.RefreshMilliHz > 0)
        {
            resource.SendRefresh(mode.RefreshMilliHz);
        }

        if (preferred)
        {
            resource.SendPreferred();
        }

        if (custom && resource.Version >= 19)
        {
            resource.SendFlags(KdeOutputDeviceModeV2.Flags.Custom);
        }

        device.Modes.Add(new ModeState { Resource = resource, Mode = mode, Custom = custom });
    }

    private uint PriorityOf(IOutput output)
    {
        var ordered = OrderedOutputs();
        for (var i = 0; i < ordered.Length; i++)
        {
            if (ordered[i] == output)
            {
                return (uint)i;
            }
        }

        var unordered = 0;
        foreach (var candidate in Outputs)
        {
            if (IsOrdered(ordered, candidate))
            {
                continue;
            }

            if (candidate == output)
            {
                return (uint)(ordered.Length + unordered);
            }

            unordered++;
        }

        return (uint)(ordered.Length + unordered);
    }

    private static bool IsOrdered(ReadOnlySpan<IOutput> ordered, IOutput output)
    {
        foreach (var candidate in ordered)
        {
            if (candidate == output)
            {
                return true;
            }
        }

        return false;
    }

    private ReadOnlySpan<IOutput> OrderedOutputs()
    {
        if (_order is not { } order)
        {
            return [];
        }

        var count = order.Enumerate(_orderScratch);
        while (count < 0)
        {
            _orderScratch = new IOutput[_orderScratch.Length * 2];
            count = order.Enumerate(_orderScratch);
        }

        return _orderScratch.AsSpan(0, count);
    }

    private static string EdidBase64(IOutput output) =>
        output.EdidBytes.Length > 0 ? Convert.ToBase64String(output.EdidBytes.Span) : string.Empty;

    private static string EisaId(IOutput output) =>
        output is Backend.Drm.DrmOutput drm && drm.EdidBytes.Length > 0 && drm.Edid.Make != "unknown"
            ? drm.Edid.Make
            : string.Empty;
}
