namespace Basin.Capabilities;

public interface IInputDeviceConfiguration
{
    int Enumerate(Span<InputDeviceInfo> devices);

    bool TryGet(ulong deviceId, InputSetting setting, out InputSettingValue value);

    InputSettingResult Set(ulong deviceId, InputSetting setting, in InputSettingValue value);

    event Action<InputDeviceInfo>? DeviceAdded;

    event Action<ulong>? DeviceRemoved;
}
