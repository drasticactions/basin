using Basin.Capabilities;
using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

/// <summary>
/// The river-libinput-config-v1 global. It announces each libinput device with its whole state and applies the window manager's settings through <see cref="Configuration"/>.
/// A setting that the configuration does not support gets an unsupported result.
/// </summary>
public sealed class RiverLibinputConfig : IDisposable, IRiverLibinputConfigV1Handler, IRiverLibinputAccelConfigV1Handler
{
    private readonly RiverWindowManager _manager;
    private readonly WlGlobal _global;
    private readonly List<DeviceEntry> _devices = [];
    private IInputDeviceConfiguration? _configuration;
    private readonly Dictionary<RiverLibinputAccelConfigV1Resource, Dictionary<InputAccelCurveType, (double Step, double[] Points)>> _curves = [];
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

    /// <summary>
    /// Gets a value indicating whether a window manager holds a live binding to this global.
    /// </summary>
    public bool IsBound => _resource is { IsDestroyed: false };

    /// <summary>
    /// Gets or sets the callback that maps a configuration device id to the handle given to <see cref="RiverInputManager.AddDevice(object, string, InputDeviceType)"/>.
    /// Without it, a libinput device is announced with no link to its input device.
    /// </summary>
    public Func<ulong, object?>? InputDeviceFor { get; set; }

    /// <summary>
    /// Gets or sets the device configuration that reads and applies settings.
    /// Setting it forgets the current devices and announces every device of the new configuration. With <see langword="null"/>, no device is announced.
    /// </summary>
    public IInputDeviceConfiguration? Configuration
    {
        get => _configuration;
        set
        {
            if (_configuration is { } previous)
            {
                previous.DeviceAdded -= OnDeviceAdded;
                previous.DeviceRemoved -= RemoveDevice;
                previous.Changed -= OnChanged;
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
                live.Changed += OnChanged;
                Adopt(live);
            }
        }
    }

    /// <summary>
    /// Registers a device and announces it to the bound window manager. An id that is already registered does nothing.
    /// </summary>
    /// <param name="deviceId">The device id in <see cref="Configuration"/>.</param>
    public void AddDevice(ulong deviceId)
    {
        if (_devices.Exists(d => d.Id == deviceId))
        {
            return;
        }

        var entry = new DeviceEntry(this, deviceId);
        _devices.Add(entry);
        Announce(entry);
    }

    /// <summary>
    /// Unregisters a device and sends <c>removed</c> on its object. The object then ignores every request but destroy. An unknown id does nothing.
    /// </summary>
    /// <param name="deviceId">The device id given to <see cref="AddDevice(ulong)"/>.</param>
    public void RemoveDevice(ulong deviceId)
    {
        if (_devices.Find(d => d.Id == deviceId) is { } entry)
        {
            _devices.Remove(entry);
            entry.Removed = true;
            if (entry.Resource is { IsDestroyed: false } resource)
            {
                resource.SendRemoved();
            }

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

    /// <summary>
    /// Forgets every device and accel curve, and removes the global. A second call does nothing.
    /// </summary>
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

        var resource = new RiverLibinputDeviceV1Resource(config.Client, config.Version, 0, entry);
        entry.Resource = resource;
        config.SendLibinputDevice(resource);
        if (InputDeviceFor?.Invoke(entry.Id) is { } handle &&
            _manager.InputManager.ResourceFor(handle, config.Client) is { } inputDevice)
        {
            resource.SendInputDevice(inputDevice);
        }

        SendState(entry.Id, resource);
    }

    private void OnChanged(ulong deviceId, InputSetting setting)
    {
        if (_devices.Find(d => d.Id == deviceId) is not { Resource: { IsDestroyed: false } resource })
        {
            return;
        }

        if (SendCurrent(deviceId, resource, setting) && resource.Version >= 2)
        {
            resource.SendDone();
        }
    }

    private void SendState(ulong id, RiverLibinputDeviceV1Resource resource)
    {
        if (_configuration is not { } configuration)
        {
            return;
        }

        var sendEvents = configuration.Support(id, InputSetting.SendEvents);
        resource.SendSendEventsSupport((RiverLibinputDeviceV1.SendEventsModes)sendEvents.Mask);
        if (sendEvents.Supported)
        {
            SendDefault(id, resource, InputSetting.SendEvents);
            SendCurrent(id, resource, InputSetting.SendEvents);
        }

        var tap = configuration.Support(id, InputSetting.Tap);
        resource.SendTapSupport(tap.Supported ? (int)tap.Count : 0);
        if (tap.Supported)
        {
            Both(id, resource, InputSetting.Tap);
            Both(id, resource, InputSetting.TapButtonMap);
            Both(id, resource, InputSetting.Drag);
            Both(id, resource, InputSetting.DragLock);
        }

        var drag = configuration.Support(id, InputSetting.ThreeFingerDrag);
        resource.SendThreeFingerDragSupport(drag.Supported ? (int)drag.Count : 0);
        if (drag.Supported)
        {
            Both(id, resource, InputSetting.ThreeFingerDrag);
        }

        var matrix = configuration.Support(id, InputSetting.CalibrationMatrix).Supported;
        resource.SendCalibrationMatrixSupport(matrix ? 1 : 0);
        if (matrix)
        {
            Both(id, resource, InputSetting.CalibrationMatrix);
        }

        var accel = configuration.Support(id, InputSetting.AccelProfile);
        resource.SendAccelProfilesSupport((RiverLibinputDeviceV1.AccelProfiles)(accel.Supported ? accel.Mask : 0));
        if (accel.Supported && accel.Mask != 0)
        {
            Both(id, resource, InputSetting.AccelProfile);
            Both(id, resource, InputSetting.AccelSpeed);
        }

        Flag(id, resource, InputSetting.NaturalScroll, resource.SendNaturalScrollSupport);
        Flag(id, resource, InputSetting.LeftHanded, resource.SendLeftHandedSupport);

        var click = configuration.Support(id, InputSetting.ClickMethod);
        resource.SendClickMethodSupport((RiverLibinputDeviceV1.ClickMethods)(click.Supported ? click.Mask : 0));
        if (click.Supported && click.Mask != 0)
        {
            Both(id, resource, InputSetting.ClickMethod);
            if (configuration.Support(id, InputSetting.ClickfingerButtonMap).Supported)
            {
                Both(id, resource, InputSetting.ClickfingerButtonMap);
            }
        }

        Flag(id, resource, InputSetting.MiddleEmulation, resource.SendMiddleEmulationSupport);

        var scroll = configuration.Support(id, InputSetting.ScrollMethod);
        resource.SendScrollMethodSupport((RiverLibinputDeviceV1.ScrollMethods)(scroll.Supported ? scroll.Mask : 0));
        if (scroll.Supported && scroll.Mask != 0)
        {
            Both(id, resource, InputSetting.ScrollMethod);
            if (configuration.Support(id, InputSetting.ScrollButton).Supported)
            {
                Both(id, resource, InputSetting.ScrollButton);
                Both(id, resource, InputSetting.ScrollButtonLock);
            }
        }

        Flag(id, resource, InputSetting.DisableWhileTyping, resource.SendDwtSupport);
        Flag(id, resource, InputSetting.DisableWhileTrackpointing, resource.SendDwtpSupport);
        Flag(id, resource, InputSetting.Rotation, resource.SendRotationSupport);

        if (resource.Version >= 2)
        {
            resource.SendDone();
        }
    }

    private void Flag(ulong id, RiverLibinputDeviceV1Resource resource, InputSetting setting, Action<int> support)
    {
        var supported = _configuration!.Support(id, setting).Supported;
        support(supported ? 1 : 0);
        if (supported)
        {
            Both(id, resource, setting);
        }
    }

    private void Both(ulong id, RiverLibinputDeviceV1Resource resource, InputSetting setting)
    {
        SendDefault(id, resource, setting);
        SendCurrent(id, resource, setting);
    }

    private void SendDefault(ulong id, RiverLibinputDeviceV1Resource resource, InputSetting setting)
    {
        if (_configuration!.TryGetDefault(id, setting, out var value))
        {
            Send(resource, setting, value, current: false);
        }
    }

    private bool SendCurrent(ulong id, RiverLibinputDeviceV1Resource resource, InputSetting setting)
    {
        if (!InputSettingNames.IsLibinput(setting) || _configuration?.TryGet(id, setting, out var value) != true)
        {
            return false;
        }

        Send(resource, setting, value, current: true);
        return true;
    }

    private static void Send(RiverLibinputDeviceV1Resource resource, InputSetting setting, in InputSettingValue value, bool current)
    {
        var raw = value.Value;
        switch (setting)
        {
            case InputSetting.SendEvents:
                if (current)
                {
                    resource.SendSendEventsCurrent((RiverLibinputDeviceV1.SendEventsModes)raw);
                }
                else
                {
                    resource.SendSendEventsDefault((RiverLibinputDeviceV1.SendEventsModes)raw);
                }

                break;
            case InputSetting.Tap:
                if (current)
                {
                    resource.SendTapCurrent((RiverLibinputDeviceV1.TapState)raw);
                }
                else
                {
                    resource.SendTapDefault((RiverLibinputDeviceV1.TapState)raw);
                }

                break;
            case InputSetting.TapButtonMap:
                if (current)
                {
                    resource.SendTapButtonMapCurrent((RiverLibinputDeviceV1.TapButtonMap)raw);
                }
                else
                {
                    resource.SendTapButtonMapDefault((RiverLibinputDeviceV1.TapButtonMap)raw);
                }

                break;
            case InputSetting.Drag:
                if (current)
                {
                    resource.SendDragCurrent((RiverLibinputDeviceV1.DragState)raw);
                }
                else
                {
                    resource.SendDragDefault((RiverLibinputDeviceV1.DragState)raw);
                }

                break;
            case InputSetting.DragLock:
                if (current)
                {
                    resource.SendDragLockCurrent((RiverLibinputDeviceV1.DragLockState)raw);
                }
                else
                {
                    resource.SendDragLockDefault((RiverLibinputDeviceV1.DragLockState)raw);
                }

                break;
            case InputSetting.ThreeFingerDrag:
                if (current)
                {
                    resource.SendThreeFingerDragCurrent((RiverLibinputDeviceV1.ThreeFingerDragState)raw);
                }
                else
                {
                    resource.SendThreeFingerDragDefault((RiverLibinputDeviceV1.ThreeFingerDragState)raw);
                }

                break;
            case InputSetting.CalibrationMatrix:
                Span<byte> matrix = stackalloc byte[6 * sizeof(float)];
                for (var i = 0; i < 6 && i < value.Count; i++)
                {
                    BitConverter.TryWriteBytes(matrix[(i * sizeof(float))..], (float)value[i]);
                }

                if (current)
                {
                    resource.SendCalibrationMatrixCurrent(matrix);
                }
                else
                {
                    resource.SendCalibrationMatrixDefault(matrix);
                }

                break;
            case InputSetting.AccelProfile:
                if (current)
                {
                    resource.SendAccelProfileCurrent((RiverLibinputDeviceV1.AccelProfile)raw);
                }
                else
                {
                    resource.SendAccelProfileDefault((RiverLibinputDeviceV1.AccelProfile)raw);
                }

                break;
            case InputSetting.AccelSpeed:
                Span<byte> speed = stackalloc byte[sizeof(double)];
                BitConverter.TryWriteBytes(speed, value.Count > 0 ? value[0] : 0.0);
                if (current)
                {
                    resource.SendAccelSpeedCurrent(speed);
                }
                else
                {
                    resource.SendAccelSpeedDefault(speed);
                }

                break;
            case InputSetting.NaturalScroll:
                if (current)
                {
                    resource.SendNaturalScrollCurrent((RiverLibinputDeviceV1.NaturalScrollState)raw);
                }
                else
                {
                    resource.SendNaturalScrollDefault((RiverLibinputDeviceV1.NaturalScrollState)raw);
                }

                break;
            case InputSetting.LeftHanded:
                if (current)
                {
                    resource.SendLeftHandedCurrent((RiverLibinputDeviceV1.LeftHandedState)raw);
                }
                else
                {
                    resource.SendLeftHandedDefault((RiverLibinputDeviceV1.LeftHandedState)raw);
                }

                break;
            case InputSetting.ClickMethod:
                if (current)
                {
                    resource.SendClickMethodCurrent((RiverLibinputDeviceV1.ClickMethod)raw);
                }
                else
                {
                    resource.SendClickMethodDefault((RiverLibinputDeviceV1.ClickMethod)raw);
                }

                break;
            case InputSetting.ClickfingerButtonMap:
                if (current)
                {
                    resource.SendClickfingerButtonMapCurrent((RiverLibinputDeviceV1.ClickfingerButtonMap)raw);
                }
                else
                {
                    resource.SendClickfingerButtonMapDefault((RiverLibinputDeviceV1.ClickfingerButtonMap)raw);
                }

                break;
            case InputSetting.MiddleEmulation:
                if (current)
                {
                    resource.SendMiddleEmulationCurrent((RiverLibinputDeviceV1.MiddleEmulationState)raw);
                }
                else
                {
                    resource.SendMiddleEmulationDefault((RiverLibinputDeviceV1.MiddleEmulationState)raw);
                }

                break;
            case InputSetting.ScrollMethod:
                if (current)
                {
                    resource.SendScrollMethodCurrent((RiverLibinputDeviceV1.ScrollMethod)raw);
                }
                else
                {
                    resource.SendScrollMethodDefault((RiverLibinputDeviceV1.ScrollMethod)raw);
                }

                break;
            case InputSetting.ScrollButton:
                if (current)
                {
                    resource.SendScrollButtonCurrent(raw);
                }
                else
                {
                    resource.SendScrollButtonDefault(raw);
                }

                break;
            case InputSetting.ScrollButtonLock:
                if (current)
                {
                    resource.SendScrollButtonLockCurrent((RiverLibinputDeviceV1.ScrollButtonLockState)raw);
                }
                else
                {
                    resource.SendScrollButtonLockDefault((RiverLibinputDeviceV1.ScrollButtonLockState)raw);
                }

                break;
            case InputSetting.DisableWhileTyping:
                if (current)
                {
                    resource.SendDwtCurrent((RiverLibinputDeviceV1.DwtState)raw);
                }
                else
                {
                    resource.SendDwtDefault((RiverLibinputDeviceV1.DwtState)raw);
                }

                break;
            case InputSetting.DisableWhileTrackpointing:
                if (current)
                {
                    resource.SendDwtpCurrent((RiverLibinputDeviceV1.DwtpState)raw);
                }
                else
                {
                    resource.SendDwtpDefault((RiverLibinputDeviceV1.DwtpState)raw);
                }

                break;
            case InputSetting.Rotation:
                if (current)
                {
                    resource.SendRotationCurrent(raw);
                }
                else
                {
                    resource.SendRotationDefault(raw);
                }

                break;
        }
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

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new RiverLibinputConfigV1Resource(client, version, id, this);
        _resource = resource;

        resource.Destroyed += (_, _) => _resource = null;

        foreach (var device in _devices)
        {
            Announce(device);
        }
    }

    void IRiverLibinputConfigV1Handler.CreateAccelConfig(RiverLibinputConfigV1Resource resource, in RiverLibinputConfigV1Resource.CreateAccelConfigEventArgs args)
    {
        var curve = new RiverLibinputAccelConfigV1Resource(resource.Client, resource.Version, args.Id, this);
        _curves[curve] = [];
    }

    void IRiverLibinputConfigV1Handler.Stop(RiverLibinputConfigV1Resource resource)
    {
        resource.SendFinished();
        _resource = null;
    }

    void IRiverLibinputConfigV1Handler.Destroy(RiverLibinputConfigV1Resource resource) => _resource = null;

    void IRiverLibinputAccelConfigV1Handler.SetPoints(RiverLibinputAccelConfigV1Resource resource, in RiverLibinputAccelConfigV1Resource.SetPointsEventArgs args)
    {
        var result = new RiverLibinputResultV1Resource(resource.Client, resource.Version, args.Result);
        var step = ReadDouble(args.Step);
        var values = ReadDoubles(args.Points);
        if (step.Count == 0 || values is null || (uint)args.Type > (uint)InputAccelCurveType.Scroll)
        {
            resource.PostError((uint)RiverLibinputAccelConfigV1.Error.InvalidArg, "The set_points argument is not valid.");
            return;
        }

        _curves[resource][(InputAccelCurveType)(uint)args.Type] = (step[0], values);
        result.SendSuccess();
    }

    void IRiverLibinputAccelConfigV1Handler.Destroy(RiverLibinputAccelConfigV1Resource resource) => _curves.Remove(resource);

    private void ApplyCurves(WlClient client, uint version, uint id, ulong deviceId, RiverLibinputAccelConfigV1Resource? config)
    {
        var result = new RiverLibinputResultV1Resource(client, version, id);
        if (_configuration is not { } configuration || config is null || !_curves.TryGetValue(config, out var curves) || curves.Count == 0)
        {
            result.SendInvalid();
            return;
        }

        var outcome = InputSettingResult.Success;
        foreach (var (type, curve) in curves)
        {
            outcome = configuration.SetAccelCurve(deviceId, type, curve.Step, curve.Points);
            if (outcome != InputSettingResult.Success)
            {
                break;
            }
        }

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

    private static InputSettingValue ReadDouble(byte[]? data) =>
        data is { Length: sizeof(double) } ? InputSettingValue.Number(BitConverter.ToDouble(data, 0)) : default;

    private static double[]? ReadDoubles(byte[]? data)
    {
        if (data is null || data.Length % sizeof(double) != 0)
        {
            return null;
        }

        var values = new double[data.Length / sizeof(double)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToDouble(data, i * sizeof(double));
        }

        return values;
    }

    private static InputSettingValue ReadMatrix(byte[]? data)
    {
        if (data is not { Length: 6 * sizeof(float) })
        {
            return default;
        }

        Span<double> matrix = stackalloc double[6];
        for (var i = 0; i < 6; i++)
        {
            matrix[i] = BitConverter.ToSingle(data, i * sizeof(float));
        }

        return InputSettingValue.Matrix(matrix);
    }

    private sealed class DeviceEntry(RiverLibinputConfig owner, ulong id) : IRiverLibinputDeviceV1Handler
    {
        public ulong Id { get; } = id;

        public RiverLibinputDeviceV1Resource? Resource { get; set; }

        public bool Removed { get; set; }

        void IRiverLibinputDeviceV1Handler.SetSendEvents(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetSendEventsEventArgs args) =>
            Simple(resource, args.Result, InputSetting.SendEvents, (uint)args.Mode);

        void IRiverLibinputDeviceV1Handler.SetTap(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetTapEventArgs args) =>
            Simple(resource, args.Result, InputSetting.Tap, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetTapButtonMap(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetTapButtonMapEventArgs args) =>
            Simple(resource, args.Result, InputSetting.TapButtonMap, (uint)args.ButtonMap);

        void IRiverLibinputDeviceV1Handler.SetDrag(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetDragEventArgs args) =>
            Simple(resource, args.Result, InputSetting.Drag, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetDragLock(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetDragLockEventArgs args) =>
            Simple(resource, args.Result, InputSetting.DragLock, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetThreeFingerDrag(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetThreeFingerDragEventArgs args) =>
            Simple(resource, args.Result, InputSetting.ThreeFingerDrag, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetAccelProfile(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetAccelProfileEventArgs args) =>
            Simple(resource, args.Result, InputSetting.AccelProfile, (uint)args.Profile);

        void IRiverLibinputDeviceV1Handler.SetNaturalScroll(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetNaturalScrollEventArgs args) =>
            Simple(resource, args.Result, InputSetting.NaturalScroll, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetLeftHanded(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetLeftHandedEventArgs args) =>
            Simple(resource, args.Result, InputSetting.LeftHanded, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetClickMethod(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetClickMethodEventArgs args) =>
            Simple(resource, args.Result, InputSetting.ClickMethod, (uint)args.Method);

        void IRiverLibinputDeviceV1Handler.SetClickfingerButtonMap(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetClickfingerButtonMapEventArgs args) =>
            Simple(resource, args.Result, InputSetting.ClickfingerButtonMap, (uint)args.ButtonMap);

        void IRiverLibinputDeviceV1Handler.SetMiddleEmulation(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetMiddleEmulationEventArgs args) =>
            Simple(resource, args.Result, InputSetting.MiddleEmulation, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetScrollMethod(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetScrollMethodEventArgs args) =>
            Simple(resource, args.Result, InputSetting.ScrollMethod, (uint)args.Method);

        void IRiverLibinputDeviceV1Handler.SetScrollButton(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetScrollButtonEventArgs args) =>
            Simple(resource, args.Result, InputSetting.ScrollButton, args.Button);

        void IRiverLibinputDeviceV1Handler.SetScrollButtonLock(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetScrollButtonLockEventArgs args) =>
            Simple(resource, args.Result, InputSetting.ScrollButtonLock, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetDwt(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetDwtEventArgs args) =>
            Simple(resource, args.Result, InputSetting.DisableWhileTyping, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetDwtp(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetDwtpEventArgs args) =>
            Simple(resource, args.Result, InputSetting.DisableWhileTrackpointing, (uint)args.State);

        void IRiverLibinputDeviceV1Handler.SetRotation(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetRotationEventArgs args) =>
            Simple(resource, args.Result, InputSetting.Rotation, args.Angle);

        void IRiverLibinputDeviceV1Handler.SetCalibrationMatrix(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetCalibrationMatrixEventArgs args) =>
            Numeric(resource, args.Result, InputSetting.CalibrationMatrix, ReadMatrix(args.Matrix));

        void IRiverLibinputDeviceV1Handler.SetAccelSpeed(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.SetAccelSpeedEventArgs args) =>
            Numeric(resource, args.Result, InputSetting.AccelSpeed, ReadDouble(args.Speed));

        void IRiverLibinputDeviceV1Handler.ApplyAccelConfig(RiverLibinputDeviceV1Resource resource, in RiverLibinputDeviceV1Resource.ApplyAccelConfigEventArgs args)
        {
            if (!Removed)
            {
                owner.ApplyCurves(resource.Client, resource.Version, args.Result, Id, args.Config);
            }
        }

        void IRiverLibinputDeviceV1Handler.Destroy(RiverLibinputDeviceV1Resource resource) => Resource = null;

        private void Simple(RiverLibinputDeviceV1Resource resource, uint id, InputSetting kind, uint value)
        {
            if (!Removed)
            {
                owner.Answer(resource.Client, resource.Version, id, Id, kind, new InputSettingValue(value));
            }
        }

        private void Numeric(RiverLibinputDeviceV1Resource resource, uint id, InputSetting kind, in InputSettingValue value)
        {
            if (Removed)
            {
                return;
            }

            if (value.Count == 0)
            {
                resource.PostError((uint)RiverLibinputDeviceV1.Error.InvalidArg, "The array argument is not valid.");
                return;
            }

            owner.Answer(resource.Client, resource.Version, id, Id, kind, value);
        }
    }
}
