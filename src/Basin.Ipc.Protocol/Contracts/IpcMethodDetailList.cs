namespace Basin.Ipc;

public readonly record struct IpcMethodDetailList(ReadOnlyMemory<IpcMethodDetail> Methods);
