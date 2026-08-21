using Basin.Capabilities;
using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

public sealed class RiverLibinputConfig : IDisposable
{
    private readonly RiverWindowManager _manager;
    private readonly WlGlobal _global;
    private readonly List<DeviceEntry> _devices = [];
    private IInputDeviceConfiguration? _configuration;
    private readonly Dictionary<RiverLibinputAccelConfigV1Resource, List<double>> _curves = [];
    private RiverLibinputConfigV1Resource? _resource;
    private bool _disposed;

    internal RiverLibinputConfig(RiverWindowManager manager, WlServerDisplay display)
    {
        _manager = manager;
        _global = display.CreateGlobal(
            RiverLibinputConfigV1.Interface,
            RiverLibinputConfigV1.Interface.Version,
            OnBind);
    }

    public bool IsBound => _resource is { IsDestroyed: false };

    public IInputDeviceConfiguration? Configuration
    {
        get => _configuration;
        set
        {
            if (_configuration is { } previous)
            {
                previous.DeviceAdded -= OnDeviceAdded;
                previous.DeviceRemoved -= RemoveDevice;
            }

            _configuration = value;
            foreach (var entry in _devices)
            {
                entry.Resource = null;
            }

            _devices.Clear();

            if (value is { } live)
            {
                live.DeviceAdded += OnDeviceAdded;
                live.DeviceRemoved += RemoveDevice;
                Adopt(live);
            }
        }
    }

    public void AddDevice(ulong deviceId)
    {
        if (_devices.Exists(d => d.Id == deviceId))
        {
            return;
        }

        var entry = new DeviceEntry(deviceId);
        _devices.Add(entry);
        Announce(entry);
    }

    public void RemoveDevice(ulong deviceId)
    {
        if (_devices.Find(d => d.Id == deviceId) is { } entry)
        {
            _devices.Remove(entry);
            entry.Resource = null;
        }
    }

    private void OnDeviceAdded(InputDeviceInfo device) => AddDevice(device.Id);

    private void Adopt(IInputDeviceConfiguration configuration)
    {
        var devices = new InputDeviceInfo[8];
        var count = configuration.Enumerate(devices);
        while (count < 0)
        {
            devices = new InputDeviceInfo[devices.Length * 2];
            count = configuration.Enumerate(devices);
        }

        for (var i = 0; i < count; i++)
        {
            AddDevice(devices[i].Id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _devices.Clear();
        _curves.Clear();
        _global.Dispose();
    }

    internal void ResetForNewManager()
    {
        _resource = null;
        _curves.Clear();
        foreach (var device in _devices)
        {
            device.Resource = null;
        }
    }

    private void Announce(DeviceEntry entry)
    {
        if (_resource is not { IsDestroyed: false } config)
        {
            return;
        }

        var resource = new RiverLibinputDeviceV1Resource(config.Client, config.Version, 0);
        entry.Resource = resource;
        Wire(entry, resource);
        config.SendLibinputDevice(resource);
    }

    private void Answer(WlClient client, uint version, uint id, ulong deviceId, InputSetting setting, in InputSettingValue value)
    {
        var result = new RiverLibinputResultV1Resource(client, version, id);
        var outcome = _configuration?.Set(deviceId, setting, value) ?? InputSettingResult.Unsupported;
        switch (outcome)
        {
            case InputSettingResult.Success:
                result.SendSuccess();
                break;
            case InputSettingResult.Invalid:
                result.SendInvalid();
                break;
            default:
                result.SendUnsupported();
                break;
        }
    }

    private void Wire(DeviceEntry entry, RiverLibinputDeviceV1Resource resource)
    {
        var client = resource.Client;
        var version = resource.Version;

        void Simple(uint id, InputSetting kind, uint value) =>
            Answer(client, version, id, entry.Id, kind, new InputSettingValue(value));

        void Numeric(uint id, InputSetting kind, IReadOnlyList<double> numbers) =>
            Answer(client, version, id, entry.Id, kind, new InputSettingValue(0, numbers));

        resource.SetSendEvents += (_, e) => Simple(e.Result, InputSetting.SendEvents, (uint)e.Mode);
        resource.SetTap += (_, e) => Simple(e.Result, InputSetting.Tap, (uint)e.State);
        resource.SetTapButtonMap += (_, e) => Simple(e.Result, InputSetting.TapButtonMap, (uint)e.ButtonMap);
        resource.SetDrag += (_, e) => Simple(e.Result, InputSetting.Drag, (uint)e.State);
        resource.SetDragLock += (_, e) => Simple(e.Result, InputSetting.DragLock, (uint)e.State);
        resource.SetThreeFingerDrag += (_, e) => Simple(e.Result, InputSetting.ThreeFingerDrag, (uint)e.State);
        resource.SetAccelProfile += (_, e) => Simple(e.Result, InputSetting.AccelProfile, (uint)e.Profile);
        resource.SetNaturalScroll += (_, e) => Simple(e.Result, InputSetting.NaturalScroll, (uint)e.State);
        resource.SetLeftHanded += (_, e) => Simple(e.Result, InputSetting.LeftHanded, (uint)e.State);
        resource.SetClickMethod += (_, e) => Simple(e.Result, InputSetting.ClickMethod, (uint)e.Method);
        resource.SetClickfingerButtonMap += (_, e) =>
            Simple(e.Result, InputSetting.ClickfingerButtonMap, (uint)e.ButtonMap);
        resource.SetMiddleEmulation += (_, e) => Simple(e.Result, InputSetting.MiddleEmulation, (uint)e.State);
        resource.SetScrollMethod += (_, e) => Simple(e.Result, InputSetting.ScrollMethod, (uint)e.Method);
        resource.SetScrollButton += (_, e) => Simple(e.Result, InputSetting.ScrollButton, e.Button);
        resource.SetScrollButtonLock += (_, e) =>
            Simple(e.Result, InputSetting.ScrollButtonLock, (uint)e.State);
        resource.SetDwt += (_, e) => Simple(e.Result, InputSetting.DisableWhileTyping, (uint)e.State);
        resource.SetDwtp += (_, e) => Simple(e.Result, InputSetting.DisableWhileTrackpointing, (uint)e.State);
        resource.SetRotation += (_, e) => Simple(e.Result, InputSetting.Rotation, e.Angle);

        resource.SetCalibrationMatrix += (_, e) =>
            Numeric(e.Result, InputSetting.CalibrationMatrix, ReadFixedArray(e.Matrix));
        resource.SetAccelSpeed += (_, e) =>
            Numeric(e.Result, InputSetting.AccelSpeed, ReadFixedArray(e.Speed));
        resource.ApplyAccelConfig += (_, e) =>
            Numeric(e.Result, InputSetting.AccelConfig, _curves.GetValueOrDefault(e.Config!) ?? []);

        resource.DestroyRequest += (_, _) => entry.Resource = null;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new RiverLibinputConfigV1Resource(client, version, id);
        _resource = resource;

        resource.CreateAccelConfig += (_, e) =>
        {
            var curve = new RiverLibinputAccelConfigV1Resource(client, version, e.Id);
            _curves[curve] = [];
            curve.SetPoints += (_, points) =>
            {
                _curves[curve] = [.. ReadFixedArray(points.Step), .. ReadFixedArray(points.Points)];
            };
            curve.DestroyRequest += (_, _) => _curves.Remove(curve);
        };

        resource.Stop += (_, _) =>
        {
            resource.SendFinished();
            _resource = null;
        };
        resource.DestroyRequest += (_, _) => _resource = null;
        resource.Destroyed += (_, _) => _resource = null;

        foreach (var device in _devices)
        {
            Announce(device);
        }
    }

    private static double[] ReadFixedArray(byte[]? data)
    {
        if (data is null || data.Length < 4)
        {
            return [];
        }

        var values = new double[data.Length / 4];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToInt32(data, i * 4) / 256.0;
        }

        return values;
    }

    private sealed class DeviceEntry(ulong id)
    {
        public ulong Id { get; } = id;

        public RiverLibinputDeviceV1Resource? Resource { get; set; }
    }
}
