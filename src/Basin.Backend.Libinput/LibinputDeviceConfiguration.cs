using Basin.Capabilities;

namespace Basin.Backend.Libinput;

public sealed class LibinputDeviceConfiguration : IInputDeviceConfiguration
{
    private readonly LibinputBackend _backend;
    private readonly Dictionary<ulong, InputDevice> _devices = [];
    private readonly Dictionary<InputDevice, ulong> _ids = [];
    private ulong _nextId;

    public LibinputDeviceConfiguration(LibinputBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        foreach (var device in backend.Devices)
        {
            Track(device);
        }

        backend.DeviceAdded += Track;
        backend.DeviceRemoved += Forget;
    }

    public event Action<InputDeviceInfo>? DeviceAdded;

    public event Action<ulong>? DeviceRemoved;

    public InputDevice? DeviceFor(ulong id) => _devices.GetValueOrDefault(id);

    public ulong IdFor(InputDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return _ids.GetValueOrDefault(device);
    }

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

    public bool TryGet(ulong deviceId, InputSetting setting, out InputSettingValue value)
    {
        value = default;
        if (!_devices.TryGetValue(deviceId, out var device))
        {
            return false;
        }

        var config = device.Config;
        switch (setting)
        {
            case InputSetting.Tap:
                value = new InputSettingValue(config.Tap.Enabled ? 1u : 0u);
                return true;
            case InputSetting.TapButtonMap:
                value = new InputSettingValue((uint)config.Tap.ButtonMap);
                return true;
            case InputSetting.Drag:
                value = new InputSettingValue(config.Tap.DragEnabled ? 1u : 0u);
                return true;
            case InputSetting.DragLock:
                value = new InputSettingValue((uint)config.Tap.DragLock);
                return true;
            case InputSetting.AccelProfile:
                value = new InputSettingValue((uint)config.Accel.Profile);
                return true;
            case InputSetting.AccelSpeed:
                value = new InputSettingValue(0, [config.Accel.Speed]);
                return true;
            case InputSetting.NaturalScroll:
                value = new InputSettingValue(config.Scroll.NaturalScrollEnabled ? 1u : 0u);
                return true;
            case InputSetting.LeftHanded:
                value = new InputSettingValue(config.LeftHanded.Enabled ? 1u : 0u);
                return true;
            case InputSetting.ClickMethod:
                value = new InputSettingValue((uint)config.Click.Method);
                return true;
            case InputSetting.MiddleEmulation:
                value = new InputSettingValue(config.MiddleEmulation.Enabled ? 1u : 0u);
                return true;
            case InputSetting.ScrollMethod:
                value = new InputSettingValue((uint)config.Scroll.Method);
                return true;
            case InputSetting.ScrollButton:
                value = new InputSettingValue(config.Scroll.Button);
                return true;
            case InputSetting.DisableWhileTyping:
                value = new InputSettingValue(config.Dwt.Enabled ? 1u : 0u);
                return true;
            case InputSetting.Rotation:
                value = new InputSettingValue(0, [config.Rotation.Angle]);
                return true;
            case InputSetting.SendEvents:
                value = new InputSettingValue((uint)config.SendEvents.Mode);
                return true;
            default:
                return false;
        }
    }

    public InputSettingResult Set(ulong deviceId, InputSetting setting, in InputSettingValue value)
    {
        if (!_devices.TryGetValue(deviceId, out var device))
        {
            return InputSettingResult.Invalid;
        }

        var config = device.Config;
        try
        {
            switch (setting)
            {
                case InputSetting.Tap:
                    config.Tap.Enabled = value.Value != 0;
                    return InputSettingResult.Success;
                case InputSetting.TapButtonMap:
                    config.Tap.ButtonMap = (global::Libinput.LibinputTapButtonMap)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.Drag:
                    config.Tap.DragEnabled = value.Value != 0;
                    return InputSettingResult.Success;
                case InputSetting.DragLock:
                    config.Tap.DragLock = (global::Libinput.LibinputDragLockState)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.AccelProfile:
                    config.Accel.Profile = (global::Libinput.LibinputAccelProfile)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.AccelSpeed:
                    if (value.Numbers.Count < 1)
                    {
                        return InputSettingResult.Invalid;
                    }

                    config.Accel.Speed = value.Numbers[0];
                    return InputSettingResult.Success;
                case InputSetting.NaturalScroll:
                    config.Scroll.NaturalScrollEnabled = value.Value != 0;
                    return InputSettingResult.Success;
                case InputSetting.LeftHanded:
                    config.LeftHanded.Enabled = value.Value != 0;
                    return InputSettingResult.Success;
                case InputSetting.ClickMethod:
                    config.Click.Method = (global::Libinput.LibinputClickMethod)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.MiddleEmulation:
                    config.MiddleEmulation.Enabled = value.Value != 0;
                    return InputSettingResult.Success;
                case InputSetting.ScrollMethod:
                    config.Scroll.Method = (global::Libinput.LibinputScrollMethod)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.ScrollButton:
                    config.Scroll.Button = value.Value;
                    return InputSettingResult.Success;
                case InputSetting.ScrollButtonLock:
                    config.Scroll.ButtonLock = value.Value != 0;
                    return InputSettingResult.Success;
                case InputSetting.DisableWhileTyping:
                    config.Dwt.Enabled = value.Value != 0;
                    return InputSettingResult.Success;
                case InputSetting.DisableWhileTrackpointing:
                    config.Dwtp.Enabled = value.Value != 0;
                    return InputSettingResult.Success;
                case InputSetting.Rotation:
                    if (value.Numbers.Count < 1)
                    {
                        return InputSettingResult.Invalid;
                    }

                    config.Rotation.Angle = (uint)value.Numbers[0];
                    return InputSettingResult.Success;
                case InputSetting.SendEvents:
                    config.SendEvents.Mode = (global::Libinput.LibinputSendEventsMode)value.Value;
                    return InputSettingResult.Success;
                case InputSetting.CalibrationMatrix:
                    if (value.Numbers.Count < 6)
                    {
                        return InputSettingResult.Invalid;
                    }

                    config.Calibration.Matrix =
                    [
                        (float)value.Numbers[0], (float)value.Numbers[1], (float)value.Numbers[2],
                        (float)value.Numbers[3], (float)value.Numbers[4], (float)value.Numbers[5],
                    ];
                    return InputSettingResult.Success;
                default:
                    return InputSettingResult.Unsupported;
            }
        }
        catch (ArgumentException)
        {
            return InputSettingResult.Invalid;
        }
        catch (InvalidOperationException)
        {
            return InputSettingResult.Unsupported;
        }
    }

    private static InputDeviceInfo Describe(ulong id, InputDevice device)
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

        return new InputDeviceInfo(id, device.Name, capabilities, device.OutputName);
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
            DeviceRemoved?.Invoke(id);
        }
    }
}
