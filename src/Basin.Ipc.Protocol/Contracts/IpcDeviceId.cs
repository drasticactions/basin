namespace Basin.Ipc;

/// <summary>
/// The data of the <c>devices/removed</c> event.
/// </summary>
/// <param name="Id">The id of the device that was removed.</param>
public readonly record struct IpcDeviceId(ulong Id);
