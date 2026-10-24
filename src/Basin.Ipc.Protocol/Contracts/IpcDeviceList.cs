namespace Basin.Ipc;

/// <summary>
/// The reply of <c>devices/list</c>.
/// </summary>
/// <param name="Devices">Every input device.</param>
public readonly record struct IpcDeviceList(ReadOnlyMemory<IpcDevice> Devices);
