namespace Basin.Ipc;

/// <summary>
/// The reply of <c>process/list</c>.
/// </summary>
/// <param name="Processes">Every launch that the tracker recorded, running or ended.</param>
public readonly record struct IpcProcessList(ReadOnlyMemory<IpcProcess> Processes);
