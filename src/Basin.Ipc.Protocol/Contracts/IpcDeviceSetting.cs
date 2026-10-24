namespace Basin.Ipc;

/// <summary>
/// One setting that a device supports, in the reply of <c>devices/get</c>.
/// </summary>
/// <param name="Key">The setting key.</param>
/// <param name="Current">The value word in force. It is empty when the value cannot be read.</param>
/// <param name="Default">The value word of the device default. It is empty when the default cannot be read.</param>
/// <param name="Source">The layer of the current value: <c>default</c>, <c>config</c> or <c>runtime</c>.</param>
/// <param name="Values">The words that the setting takes on this device. It is empty for a numeric setting.</param>
public readonly record struct IpcDeviceSetting(
    string Key,
    string Current,
    string Default,
    string Source,
    ReadOnlyMemory<string> Values);
