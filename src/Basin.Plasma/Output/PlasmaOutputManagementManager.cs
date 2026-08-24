using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class PlasmaOutputManagementManager : IDisposable
{
    public const int Version = 21;

    private readonly PlasmaOutputDeviceManager? _devices;
    private readonly IOutputConfiguration? _configuration;
    private readonly WlGlobal _global;
    private readonly Dictionary<KdeModeListV2Resource, ModeListState> _modeLists = [];

    private sealed class ModeListState
    {
        public readonly List<OutputMode> Modes = [];
        public uint? Width;
        public uint? Height;
        public uint? Rate;
    }

    public PlasmaOutputManagementManager(
        WlServerDisplay display, PlasmaOutputDeviceManager? devices, IOutputConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(display);
        _devices = devices;
        _configuration = configuration;
        _global = display.CreateGlobal(KdeOutputManagementV2.Interface, Version, OnBind);
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new KdeOutputManagementV2Resource(client, version, id);
        resource.CreateConfiguration += (_, e) =>
            _ = new Configuration(this, new KdeOutputConfigurationV2Resource(client, resource.Version, e.Id));
        resource.CreateModeList += (_, e) =>
            TrackModeList(new KdeModeListV2Resource(client, resource.Version, e.Id));
    }

    private void TrackModeList(KdeModeListV2Resource resource)
    {
        var state = new ModeListState();
        _modeLists[resource] = state;
        resource.Destroyed += (_, _) => _modeLists.Remove(resource);
        resource.SetResolution += (_, e) =>
        {
            state.Width = e.Width;
            state.Height = e.Height;
        };
        resource.SetRefreshRate += (_, e) => state.Rate = e.Rate;
        resource.AddMode += (_, _) =>
        {
            if (state.Width is not { } width || state.Height is not { } height || state.Rate is not { } rate)
            {
                resource.PostError(
                    (uint)KdeModeListV2.Error.MissingParameters,
                    "add_mode needs set_resolution and set_refresh_rate first");
                return;
            }

            state.Modes.Add(new OutputMode((int)width, (int)height, (int)rate));
        };
    }

    private sealed class Configuration
    {
        private readonly PlasmaOutputManagementManager _owner;
        private readonly KdeOutputConfigurationV2Resource _resource;
        private readonly List<OutputConfigurationEntry> _entries = [];
        private bool _used;
        private string? _invalid;

        public Configuration(PlasmaOutputManagementManager owner, KdeOutputConfigurationV2Resource resource)
        {
            _owner = owner;
            _resource = resource;

            resource.Enable += (_, e) => Update(e.Outputdevice, entry => entry with { Enabled = e.Enable == 1 });
            resource.Mode += (_, e) =>
            {
                if (e.Outputdevice is not { } device || e.Mode is not { } mode ||
                    _owner._devices is not { } devices || !devices.TryResolveMode(device, mode, out var resolved))
                {
                    _invalid ??= "the configuration names a mode the output does not have";
                    return;
                }

                Update(device, entry => entry with { Mode = resolved });
            };
            resource.Transform += (_, e) =>
                Update(e.Outputdevice, entry => entry with { Transform = (OutputTransform)e.Transform });
            resource.Position += (_, e) =>
                Update(e.Outputdevice, entry => entry with { Position = new Point(e.X, e.Y) });
            resource.Scale += (_, e) => Update(e.Outputdevice, entry => entry with { Scale = e.Scale.ToDouble() });
            resource.Overscan += (_, e) => Update(e.Outputdevice, entry => entry with { Overscan = e.Overscan });
            resource.SetVrrPolicy += (_, e) =>
                Update(e.Outputdevice, entry => entry with { VrrPolicy = (OutputVrrPolicy)e.Policy });
            resource.SetRgbRange += (_, e) =>
                Update(e.Outputdevice, entry => entry with { RgbRange = (OutputRgbRange)e.RgbRange });
            resource.SetPrimaryOutput += (_, e) => Update(e.Output, entry => entry with { Primary = true });
            resource.SetPriority += (_, e) => Update(e.Outputdevice, entry => entry with { Priority = e.Priority });
            resource.SetHighDynamicRange += (_, e) =>
                Update(e.Outputdevice, entry => entry with { HighDynamicRange = e.EnableHdr == 1 });
            resource.SetSdrBrightness += (_, e) =>
                Update(e.Outputdevice, entry => entry with { SdrBrightnessNits = e.SdrBrightness });
            resource.SetWideColorGamut += (_, e) =>
                Update(e.Outputdevice, entry => entry with { WideColorGamut = e.EnableWcg == 1 });
            resource.SetAutoRotatePolicy += (_, e) =>
                Update(e.Outputdevice, entry => entry with { AutoRotate = (OutputAutoRotatePolicy)e.Policy });
            resource.SetIccProfilePath += (_, e) =>
                Update(e.Outputdevice, entry => entry with { IccProfilePath = e.ProfilePath });
            resource.SetBrightnessOverrides += (_, e) => Update(e.Outputdevice, entry => entry with
            {
                BrightnessOverrides = new OutputBrightnessOverrides(
                    e.MaxPeakBrightness, e.MaxFrameAverageBrightness, e.MinBrightness),
            });
            resource.SetSdrGamutWideness += (_, e) =>
                Update(e.Outputdevice, entry => entry with { SdrGamutWideness = e.GamutWideness });
            resource.SetColorProfileSource += (_, e) => Update(e.Outputdevice, entry => entry with
            {
                ColorProfileSource = (OutputColorProfileSource)e.ColorProfileSource,
            });
            resource.SetBrightness += (_, e) =>
                Update(e.Outputdevice, entry => entry with { Brightness = e.Brightness });
            resource.SetColorPowerTradeoff += (_, e) => Update(e.Outputdevice, entry => entry with
            {
                ColorPowerTradeoff = (OutputColorPowerTradeoff)e.Preference,
            });
            resource.SetDimming += (_, e) => Update(e.Outputdevice, entry => entry with { Dimming = e.Multiplier });
            resource.SetReplicationSource += (_, e) =>
                Update(e.Outputdevice, entry => entry with { ReplicationSourceUuid = e.Source });
            resource.SetDdcCiAllowed += (_, e) =>
                Update(e.Outputdevice, entry => entry with { DdcCiAllowed = e.Allowed == 1 });
            resource.SetMaxBitsPerColor += (_, e) =>
                Update(e.Outputdevice, entry => entry with { MaxBitsPerColor = e.MaxBpc });
            resource.SetEdrPolicy += (_, e) =>
                Update(e.Outputdevice, entry => entry with { EdrPolicy = (OutputEdrPolicy)e.Policy });
            resource.SetSharpness += (_, e) =>
                Update(e.Outputdevice, entry => entry with { Sharpness = e.Sharpness });
            resource.SetCustomModes += (_, e) =>
            {
                if (e.Modes is not { } modes || !_owner._modeLists.TryGetValue(modes, out var list))
                {
                    _invalid ??= "the configuration names a mode list that is gone";
                    return;
                }

                var snapshot = list.Modes.ToArray();
                Update(e.Outputdevice, entry => entry with { CustomModes = snapshot });
            };
            resource.SetAutoBrightness += (_, e) =>
                Update(e.Outputdevice, entry => entry with { AutoBrightness = e.Enabled == 1 });
            resource.SetHdrIccProfilePath += (_, e) =>
                Update(e.Outputdevice, entry => entry with { HdrIccProfilePath = e.ProfilePath });
            resource.SetHdrColorProfileSource += (_, e) => Update(e.Outputdevice, entry => entry with
            {
                HdrColorProfileSource = (OutputColorProfileSource)e.ColorProfileSource,
            });
            resource.SetAbmLevel += (_, e) => Update(e.Outputdevice, entry => entry with { AbmLevel = e.Level });
            resource.Apply += (_, _) => OnApply();
        }

        private void Update(KdeOutputDeviceV2Resource? device, Func<OutputConfigurationEntry, OutputConfigurationEntry> change)
        {
            if (device is null || _owner._devices is not { } devices || !devices.TryResolveDevice(device, out var output))
            {
                _invalid ??= "the configuration names an output device that is gone";
                return;
            }

            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Output == output)
                {
                    _entries[i] = change(_entries[i]);
                    return;
                }
            }

            _entries.Add(change(new OutputConfigurationEntry
            {
                Output = output,
                Enabled = PlasmaOutputDeviceManager.IsEnabled(output),
            }));
        }

        private void OnApply()
        {
            if (_used)
            {
                _resource.PostError(
                    (uint)KdeOutputConfigurationV2.Error.AlreadyApplied, "the configuration is already applied");
                return;
            }

            _used = true;
            if (_owner._configuration is not { } configuration)
            {
                Fail("output configuration is not supported");
                return;
            }

            if (_invalid is { } invalid)
            {
                Fail(invalid);
                return;
            }

            var entries = new List<OutputConfigurationEntry>(_entries.Count);
            foreach (var entry in _entries)
            {
                if (Sanitize(entry, configuration.Supported(entry.Output), out var sanitized) is { } unsupported)
                {
                    Fail(unsupported);
                    return;
                }

                entries.Add(sanitized);
            }

            if (!configuration.Test(entries))
            {
                Fail(configuration.LastFailureReason ?? "the output configuration failed the test");
                return;
            }

            if (!configuration.Apply(entries))
            {
                Fail(configuration.LastFailureReason ?? "the output configuration failed to apply");
                return;
            }

            _resource.SendApplied();
        }

        private static string? Sanitize(
            in OutputConfigurationEntry entry, OutputConfigurationFeatures features, out OutputConfigurationEntry sanitized)
        {
            sanitized = entry;
            var missing = OutputConfigurationGate.Reject(ref sanitized, features);
            return missing is null ? null : $"{missing} is not supported on this output";
        }

        private void Fail(string? reason)
        {
            if (reason is not null && _resource.Version >= 12)
            {
                _resource.SendFailureReason(reason);
            }

            _resource.SendFailed();
        }
    }
}
