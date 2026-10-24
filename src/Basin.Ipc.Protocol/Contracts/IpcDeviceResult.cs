namespace Basin.Ipc;

/// <summary>
/// The reply of <c>devices/set</c> and <c>devices/reset</c>.
/// </summary>
/// <param name="Results">One outcome for each device and key.</param>
public readonly record struct IpcDeviceResult(ReadOnlyMemory<IpcDeviceOutcome> Results);
