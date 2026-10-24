namespace Basin.Capabilities;

/// <summary>
/// Tells where the current value of each device setting comes from.
/// </summary>
public interface IInputSettingSources
{
    /// <summary>Finds the layer that supplies the current value of a setting.</summary>
    /// <param name="deviceId">The <see cref="InputDeviceInfo.Id"/> of the device.</param>
    /// <param name="setting">The setting to examine.</param>
    /// <returns>The layer. An unknown device gives <see cref="InputSettingLayer.Default"/>.</returns>
    InputSettingLayer SourceOf(ulong deviceId, InputSetting setting);

    /// <summary>Lists the configuration rules that match a device.</summary>
    /// <param name="deviceId">The <see cref="InputDeviceInfo.Id"/> of the device.</param>
    /// <returns>The indexes of the matching rules in ascending order. An unknown device gives an empty list.</returns>
    IReadOnlyList<int> RulesFor(ulong deviceId);
}
