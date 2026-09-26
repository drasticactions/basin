namespace Basin.Ipc;

public readonly record struct IpcWindowList(ReadOnlyMemory<IpcWindow> Windows);
