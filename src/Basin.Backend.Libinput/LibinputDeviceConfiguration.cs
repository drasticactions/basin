using Basin.Capabilities;
using Libinput;
using Native = Libinput.Native;

namespace Basin.Backend.Libinput;

/// <summary>
/// Implements <see cref="IInputDeviceConfiguration"/> straight over libinput. Each device gets an ID that stays valid until libinput removes it.
/// </summary>
public sealed unsafe class LibinputDeviceConfiguration : IInputDeviceConfiguration
{
    private readonly Dictionary<ulong, InputDevice> _devices = [];
    private readonly Dictionary<InputDevice, ulong> _ids = [];
    private readonly Dictionary<ulong, Dictionary<InputAccelCurveType, (double Step, double[] Points)>> _curves = [];
    private ulong _nextId;

    internal LibinputDeviceConfiguration(LibinputBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        foreach (var device in backend.Devices)
        {
            Track(device);
        }

        backend.DeviceAdded += Track;
        backend.DeviceRemoved += Forget;
    }

    /// <inheritdoc/>
    public event Action<InputDeviceInfo>? DeviceAdded;

    /// <inheritdoc/>
    public event Action<ulong>? DeviceRemoved;

    /// <inheritdoc/>
    public event Action<ulong, InputSetting>? Changed;

    /// <summary>Finds the device that has the given ID.</summary>
    /// <param name="id">The device ID from this configuration.</param>
    /// <returns>The device, or <see langword="null"/> when the ID is unknown or the device was removed.</returns>
    public InputDevice? DeviceFor(ulong id) => _devices.GetValueOrDefault(id);

    /// <summary>Finds the ID of a device.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The device ID, or 0 when the configuration does not track the device.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public ulong IdFor(InputDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return _ids.GetValueOrDefault(device);
    }

    /// <inheritdoc/>
    public int Enumerate(Span<InputDeviceInfo> devices)
    {
        if (_devices.Count > devices.Length)
        {
            return -1;
        }

        var written = 0;
        foreach (var (id, device) in _devices)
        {
            devices[written++] = Describe(id, device);
        }

        return written;
    }

    /// <inheritdoc/>
    public InputSettingSupport Support(ulong deviceId, InputSetting setting)
    {
        if (!_devices.TryGetValue(deviceId, out var device) || device.Native.IsDisposed)
        {
            return InputSettingSupport.None;
        }

        var config = device.Config;
        switch (setting)
        {
            case InputSetting.SendEvents:
                var modes = (uint)config.SendEvents.Modes;
                return new InputSettingSupport(true, modes);
            case InputSetting.Tap:
            case InputSetting.TapButtonMap:
            case InputSetting.Drag:
            case InputSetting.DragLock:
                var fingers = config.Tap.FingerCount;
                return fingers > 0 ? new InputSettingSupport(true, 0, (uint)fingers) : InputSettingSupport.None;
            case InputSetting.ThreeFingerDrag:
                var dragFingers = config.ThreeFingerDrag.FingerCount;
                return dragFingers >= 3 ? new InputSettingSupport(true, 0, (uint)dragFingers) : InputSettingSupport.None;
            case InputSetting.CalibrationMatrix:
                return config.Calibration.HasMatrix ? InputSettingSupport.Yes : InputSettingSupport.None;
            case InputSetting.AccelProfile:
            case InputSetting.AccelSpeed:
                var profiles = (uint)config.Accel.Profiles;
                return profiles != 0 && config.Accel.IsAvailable
                    ? new InputSettingSupport(true, profiles)
                    : InputSettingSupport.None;
            case InputSetting.NaturalScroll:
                return config.Scroll.HasNaturalScroll ? InputSettingSupport.Yes : InputSettingSupport.None;
            case InputSetting.LeftHanded:
                return config.LeftHanded.IsAvailable ? InputSettingSupport.Yes : InputSettingSupport.None;
            case InputSetting.ClickMethod:
                var clicks = (uint)config.Click.Methods;
                return clicks != 0 ? new InputSettingSupport(true, clicks) : InputSettingSupport.None;
            case InputSetting.ClickfingerButtonMap:
                return (config.Click.Methods & LibinputClickMethod.Clickfinger) != 0
                    ? InputSettingSupport.Yes
                    : InputSettingSupport.None;
            case InputSetting.MiddleEmulation:
                return config.MiddleEmulation.IsAvailable ? InputSettingSupport.Yes : InputSettingSupport.None;
            case InputSetting.ScrollMethod:
                var scrolls = (uint)config.Scroll.Methods;
                return scrolls != 0 ? new InputSettingSupport(true, scrolls) : InputSettingSupport.None;
            case InputSetting.ScrollButton:
            case InputSetting.ScrollButtonLock:
                return (config.Scroll.Methods & LibinputScrollMethod.OnButtonDown) != 0
                    ? InputSettingSupport.Yes
                    : InputSettingSupport.None;
            case InputSetting.DisableWhileTyping:
                return config.Dwt.IsAvailable ? InputSettingSupport.Yes : InputSettingSupport.None;
            case InputSetting.DisableWhileTrackpointing:
                return config.Dwtp.IsAvailable ? InputSettingSupport.Yes : InputSettingSupport.None;
            case InputSetting.Rotation:
                return config.Rotation.IsAvailable ? InputSettingSupport.Yes : InputSettingSupport.None;
            default:
                return InputSettingSupport.None;
        }
    }

    /// <inheritdoc/>
    public bool TryGet(ulong deviceId, InputSetting setting, out InputSettingValue value) =>
        Read(deviceId, setting, current: true, out value);

    /// <inheritdoc/>
    public bool TryGetDefault(ulong deviceId, InputSetting setting, out InputSettingValue value) =>
        Read(deviceId, setting, current: false, out value);

    /// <inheritdoc/>
    public InputSettingResult Set(ulong deviceId, InputSetting setting, in InputSettingValue value)
    {
        if (!_devices.TryGetValue(deviceId, out var device))
        {
            return InputSettingResult.Invalid;
        }

        if (!Support(deviceId, setting).Supported)
        {
            return InputSettingResult.Unsupported;
        }

        var before = Read(deviceId, setting, current: true, out var previous);
        var outcome = Write(device, setting, value);
        if (outcome == InputSettingResult.Success &&
            (!before || !Read(deviceId, setting, current: true, out var now) || !now.Equals(previous)))
        {
            Changed?.Invoke(deviceId, setting);
        }

        return outcome;
    }

    /// <inheritdoc/>
    public InputSettingResult Reset(ulong deviceId, InputSetting setting)
    {
        if (!_devices.ContainsKey(deviceId))
        {
            return InputSettingResult.Invalid;
        }

        if (!Read(deviceId, setting, current: false, out var fallback))
        {
            return InputSettingResult.Unsupported;
        }

        if (setting == InputSetting.AccelProfile)
        {
            _curves.Remove(deviceId);
        }

        return Set(deviceId, setting, fallback);
    }

    /// <inheritdoc/>
    public InputSettingResult SetAccelCurve(ulong deviceId, InputAccelCurveType type, double step, ReadOnlySpan<double> points)
    {
        if (!_devices.TryGetValue(deviceId, out var device))
        {
            return InputSettingResult.Invalid;
        }

        if ((device.Config.Accel.Profiles & LibinputAccelProfile.Custom) == 0)
        {
            return InputSettingResult.Unsupported;
        }

        if (!_curves.TryGetValue(deviceId, out var curves))
        {
            curves = [];
            _curves[deviceId] = curves;
        }

        var previous = curves.TryGetValue(type, out var saved) ? saved : ((double, double[])?)null;
        curves[type] = (step, points.ToArray());
        try
        {
            using var accel = new LibinputCustomAccel(LibinputAccelProfile.Custom);
            foreach (var (curveType, curve) in curves)
            {
                accel.SetPoints((LibinputAccelType)curveType, curve.Step, curve.Points);
            }

            var before = device.Config.Accel.Profile;
            device.Config.Accel.Apply(accel);
            if (before != device.Config.Accel.Profile)
            {
                Changed?.Invoke(deviceId, InputSetting.AccelProfile);
            }

            return InputSettingResult.Success;
        }
        catch (LibinputException)
        {
            if (previous is { } restore)
            {
                curves[type] = restore;
            }
            else
            {
                curves.Remove(type);
            }

            return InputSettingResult.Invalid;
        }
    }

    private bool Read(ulong deviceId, InputSetting setting, bool current, out InputSettingValue value)
    {
        value = default;
        if (!_devices.TryGetValue(deviceId, out var device) || !Support(deviceId, setting).Supported)
        {
            return false;
        }

        var config = device.Config;
        var handle = (Native.libinput_device*)device.Native.NativeHandle;
        switch (setting)
        {
            case InputSetting.SendEvents:
                value = InputSettingValue.Of((uint)(current ? config.SendEvents.Mode : config.SendEvents.DefaultMode));
                return true;
            case InputSetting.Tap:
                value = InputSettingValue.Flag(current ? config.Tap.Enabled : config.Tap.DefaultEnabled);
                return true;
            case InputSetting.TapButtonMap:
                value = InputSettingValue.Of((uint)(current ? config.Tap.ButtonMap : config.Tap.DefaultButtonMap));
                return true;
            case InputSetting.Drag:
                value = InputSettingValue.Flag(current ? config.Tap.DragEnabled : config.Tap.DefaultDragEnabled);
                return true;
            case InputSetting.DragLock:
                value = InputSettingValue.Of((uint)(current ? config.Tap.DragLock : config.Tap.DefaultDragLock));
                return true;
            case InputSetting.ThreeFingerDrag:
                value = InputSettingValue.Of((uint)(current ? config.ThreeFingerDrag.State : config.ThreeFingerDrag.DefaultState));
                return true;
            case InputSetting.CalibrationMatrix:
                var matrix = stackalloc float[6];
                _ = current
                    ? Native.Libinput.libinput_device_config_calibration_get_matrix(handle, matrix)
                    : Native.Libinput.libinput_device_config_calibration_get_default_matrix(handle, matrix);
                value = InputSettingValue.Matrix([matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]]);
                return true;
            case InputSetting.AccelProfile:
                value = InputSettingValue.Of((uint)(current ? config.Accel.Profile : config.Accel.DefaultProfile));
                return true;
            case InputSetting.AccelSpeed:
                value = InputSettingValue.Number(current ? config.Accel.Speed : config.Accel.DefaultSpeed);
                return true;
            case InputSetting.NaturalScroll:
                value = InputSettingValue.Flag(current ? config.Scroll.NaturalScrollEnabled : config.Scroll.DefaultNaturalScrollEnabled);
                return true;
            case InputSetting.LeftHanded:
                value = InputSettingValue.Flag(current ? config.LeftHanded.Enabled : config.LeftHanded.DefaultEnabled);
                return true;
            case InputSetting.ClickMethod:
                value = InputSettingValue.Of((uint)(current ? config.Click.Method : config.Click.DefaultMethod));
                return true;
            case InputSetting.ClickfingerButtonMap:
                value = InputSettingValue.Of((uint)(current ? config.Click.ClickfingerButtonMap : config.Click.DefaultClickfingerButtonMap));
                return true;
            case InputSetting.MiddleEmulation:
                value = InputSettingValue.Flag(current ? config.MiddleEmulation.Enabled : config.MiddleEmulation.DefaultEnabled);
                return true;
            case InputSetting.ScrollMethod:
                value = InputSettingValue.Of((uint)(current ? config.Scroll.Method : config.Scroll.DefaultMethod));
                return true;
            case InputSetting.ScrollButton:
                value = InputSettingValue.Of(current ? config.Scroll.Button : config.Scroll.DefaultButton);
                return true;
            case InputSetting.ScrollButtonLock:
                value = InputSettingValue.Flag(current ? config.Scroll.ButtonLock : config.Scroll.DefaultButtonLock);
                return true;
            case InputSetting.DisableWhileTyping:
                value = InputSettingValue.Flag(current ? config.Dwt.Enabled : config.Dwt.DefaultEnabled);
                return true;
            case InputSetting.DisableWhileTrackpointing:
                value = InputSettingValue.Flag(current ? config.Dwtp.Enabled : config.Dwtp.DefaultEnabled);
                return true;
            case InputSetting.Rotation:
                value = InputSettingValue.Of(current ? config.Rotation.Angle : config.Rotation.DefaultAngle);
                return true;
            default:
                return false;
        }
    }

    private static InputSettingResult Write(InputDevice device, InputSetting setting, in InputSettingValue value)
    {
        var config = device.Config;
        try
        {
            switch (setting)
            {
                case InputSetting.SendEvents:
                    config.SendEvents.Mode = (LibinputSendEventsMode)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.Tap:
                    config.Tap.Enabled = value.IsOn;
                    return InputSettingResult.Success;
                case InputSetting.TapButtonMap:
                    config.Tap.ButtonMap = (LibinputTapButtonMap)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.Drag:
                    config.Tap.DragEnabled = value.IsOn;
                    return InputSettingResult.Success;
                case InputSetting.DragLock:
                    config.Tap.DragLock = (LibinputDragLockState)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.ThreeFingerDrag:
                    config.ThreeFingerDrag.State = (LibinputThreeFingerDragState)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.CalibrationMatrix:
                    if (value.Count != InputSettingValue.MaxNumbers)
                    {
                        return InputSettingResult.Invalid;
                    }

                    var matrix = stackalloc float[6];
                    for (var i = 0; i < 6; i++)
                    {
                        matrix[i] = (float)value[i];
                    }

                    return Native.Libinput.libinput_device_config_calibration_set_matrix((Native.libinput_device*)device.Native.NativeHandle, matrix)
                        == Native.libinput_config_status.LIBINPUT_CONFIG_STATUS_SUCCESS
                        ? InputSettingResult.Success
                        : InputSettingResult.Invalid;
                case InputSetting.AccelProfile:
                    config.Accel.Profile = (LibinputAccelProfile)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.AccelSpeed:
                    if (value.Count < 1)
                    {
                        return InputSettingResult.Invalid;
                    }

                    config.Accel.Speed = value[0];
                    return InputSettingResult.Success;
                case InputSetting.NaturalScroll:
                    config.Scroll.NaturalScrollEnabled = value.IsOn;
                    return InputSettingResult.Success;
                case InputSetting.LeftHanded:
                    config.LeftHanded.Enabled = value.IsOn;
                    return InputSettingResult.Success;
                case InputSetting.ClickMethod:
                    config.Click.Method = (LibinputClickMethod)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.ClickfingerButtonMap:
                    config.Click.ClickfingerButtonMap = (LibinputClickfingerButtonMap)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.MiddleEmulation:
                    config.MiddleEmulation.Enabled = value.IsOn;
                    return InputSettingResult.Success;
                case InputSetting.ScrollMethod:
                    config.Scroll.Method = (LibinputScrollMethod)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.ScrollButton:
                    config.Scroll.Button = value.Value;
                    return InputSettingResult.Success;
                case InputSetting.ScrollButtonLock:
                    config.Scroll.ButtonLock = value.IsOn;
                    return InputSettingResult.Success;
                case InputSetting.DisableWhileTyping:
                    config.Dwt.Enabled = value.IsOn;
                    return InputSettingResult.Success;
                case InputSetting.DisableWhileTrackpointing:
                    config.Dwtp.Enabled = value.IsOn;
                    return InputSettingResult.Success;
                case InputSetting.Rotation:
                    config.Rotation.Angle = value.Value;
                    return InputSettingResult.Success;
                default:
                    return InputSettingResult.Unsupported;
            }
        }
        catch (LibinputException)
        {
            return InputSettingResult.Invalid;
        }
        catch (ObjectDisposedException)
        {
            return InputSettingResult.Invalid;
        }
    }

    /// <summary>Builds the capability-level description of a libinput device.</summary>
    /// <param name="id">The ID to put in the description.</param>
    /// <param name="device">The device to describe.</param>
    /// <returns>The description with the capabilities, the device type and the udev identity.</returns>
    public static InputDeviceInfo Describe(ulong id, InputDevice device)
    {
        var capabilities = InputDeviceCapability.None;
        if (device.HasKeyboard)
        {
            capabilities |= InputDeviceCapability.Keyboard;
        }

        if (device.HasPointer)
        {
            capabilities |= InputDeviceCapability.Pointer;
        }

        if (device.HasTouch)
        {
            capabilities |= InputDeviceCapability.Touch;
        }

        if (device.HasGesture)
        {
            capabilities |= InputDeviceCapability.Gesture;
        }

        if (device.HasSwitch)
        {
            capabilities |= InputDeviceCapability.Switch;
        }

        if (device.HasTabletTool)
        {
            capabilities |= InputDeviceCapability.TabletTool;
        }

        if (device.HasTabletPad)
        {
            capabilities |= InputDeviceCapability.TabletPad;
        }

        var type = InputDeviceTypes.Of(capabilities, device.Native.IsDisposed ? 0 : device.Config.Tap.FingerCount);
        return new InputDeviceInfo(
            id,
            device.Name,
            capabilities,
            type,
            device.OutputName,
            device.Sysname,
            device.Vendor,
            device.Product,
            device.Bus);
    }

    private void Track(InputDevice device)
    {
        if (_ids.ContainsKey(device))
        {
            return;
        }

        var id = ++_nextId;
        _devices[id] = device;
        _ids[device] = id;
        DeviceAdded?.Invoke(Describe(id, device));
    }

    private void Forget(InputDevice device)
    {
        if (_ids.Remove(device, out var id))
        {
            _devices.Remove(id);
            _curves.Remove(id);
            DeviceRemoved?.Invoke(id);
        }
    }
}
