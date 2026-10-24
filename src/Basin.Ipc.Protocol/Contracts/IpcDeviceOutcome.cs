namespace Basin.Ipc;

/// <summary>
/// The result of one key on one device, in the reply of <c>devices/set</c> or <c>devices/reset</c>.
/// </summary>
/// <param name="Device">The device id. It is 0 when a <c>type</c> target matched no device.</param>
/// <param name="Key">The setting key.</param>
/// <param name="Result"><c>ok</c>, <c>unsupported</c> or <c>invalid</c>.</param>
public readonly record struct IpcDeviceOutcome(ulong Device, string Key, string Result);
