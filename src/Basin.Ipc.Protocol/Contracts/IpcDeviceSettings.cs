namespace Basin.Ipc;

/// <summary>
/// The reply of <c>devices/get</c>.
/// </summary>
/// <param name="Device">The device id.</param>
/// <param name="Name">The device name.</param>
/// <param name="Settings">Each setting that the device supports. A setting that the device lacks is not in the list.</param>
public readonly record struct IpcDeviceSettings(ulong Device, string Name, ReadOnlyMemory<IpcDeviceSetting> Settings);
