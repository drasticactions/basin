namespace Basin.Ipc;

/// <summary>
/// The reply of <c>windows/list</c>.
/// </summary>
/// <param name="Windows">Every window that the toplevel model reports, in the model's order.</param>
public readonly record struct IpcWindowList(ReadOnlyMemory<IpcWindow> Windows);
