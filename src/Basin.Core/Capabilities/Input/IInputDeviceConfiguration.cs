namespace Basin.Capabilities;

/// <summary>
/// Reads and changes the settings of physical input devices, for example tap-to-click or the keymap layout.
/// </summary>
public interface IInputDeviceConfiguration
{
    /// <summary>Copies the description of each known device into a span.</summary>
    /// <param name="devices">The destination. It must be large enough for all devices.</param>
    /// <returns>The number of devices written, or -1 when <paramref name="devices"/> is too small.</returns>
    int Enumerate(Span<InputDeviceInfo> devices);

    /// <summary>Reads the current value of a setting.</summary>
    /// <param name="deviceId">The <see cref="InputDeviceInfo.Id"/> of the device.</param>
    /// <param name="setting">The setting to read.</param>
    /// <param name="value">The current value, or the default value when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when the device is unknown or does not support the setting.</returns>
    bool TryGet(ulong deviceId, InputSetting setting, out InputSettingValue value);

    /// <summary>Reads the value that a setting has before any configuration changes it.</summary>
    /// <param name="deviceId">The <see cref="InputDeviceInfo.Id"/> of the device.</param>
    /// <param name="setting">The setting to read.</param>
    /// <param name="value">The default value, or the default of the type when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when the device is unknown or does not support the setting.</returns>
    bool TryGetDefault(ulong deviceId, InputSetting setting, out InputSettingValue value);

    /// <summary>Tells whether a device supports a setting, and which choices it supports.</summary>
    /// <param name="deviceId">The <see cref="InputDeviceInfo.Id"/> of the device.</param>
    /// <param name="setting">The setting to examine.</param>
    /// <returns>The support, or <see cref="InputSettingSupport.None"/> for an unknown device.</returns>
    InputSettingSupport Support(ulong deviceId, InputSetting setting);

    /// <summary>Applies a value to a setting of a device.</summary>
    /// <param name="deviceId">The <see cref="InputDeviceInfo.Id"/> of the device.</param>
    /// <param name="setting">The setting to change.</param>
    /// <param name="value">The new value.</param>
    /// <returns>The outcome. <see cref="InputSettingResult.Success"/> means that the device uses the value now.</returns>
    InputSettingResult Set(ulong deviceId, InputSetting setting, in InputSettingValue value);

    /// <summary>Removes a runtime change and returns the setting to its configured or default value.</summary>
    /// <param name="deviceId">The <see cref="InputDeviceInfo.Id"/> of the device.</param>
    /// <param name="setting">The setting to reset.</param>
    /// <returns>The outcome of the reset.</returns>
    InputSettingResult Reset(ulong deviceId, InputSetting setting);

    /// <summary>
    /// Sets the points of a custom pointer acceleration curve, as libinput defines it.
    /// The device must support <see cref="InputAccelProfile.Custom"/>.
    /// </summary>
    /// <param name="deviceId">The <see cref="InputDeviceInfo.Id"/> of the device.</param>
    /// <param name="type">The kind of movement that the curve applies to.</param>
    /// <param name="step">The distance in device units between two adjacent points.</param>
    /// <param name="points">The output speed at each step, starting at zero.</param>
    /// <returns>The outcome. <see cref="InputSettingResult.Invalid"/> means that the device is unknown.</returns>
    InputSettingResult SetAccelCurve(ulong deviceId, InputAccelCurveType type, double step, ReadOnlySpan<double> points);

    /// <summary>Occurs after a device is added. The argument describes the new device.</summary>
    event Action<InputDeviceInfo>? DeviceAdded;

    /// <summary>Occurs after a device is removed. The argument is the identifier of the device.</summary>
    event Action<ulong>? DeviceRemoved;

    /// <summary>Occurs after the value of a setting changes. The arguments are the device identifier and the setting.</summary>
    event Action<ulong, InputSetting>? Changed;
}
