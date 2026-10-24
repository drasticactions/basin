namespace Basin.Ipc;

/// <summary>
/// The data of the <c>devices/changed</c> event. It coalesces for each device and key in one loop iteration.
/// </summary>
/// <param name="Device">The device id.</param>
/// <param name="Key">The setting key, for example <c>accel_speed</c>.</param>
/// <param name="Value">The value word at the flush, as <c>devices/get</c> writes it.</param>
public readonly record struct IpcDeviceChanged(ulong Device, string Key, string Value);
