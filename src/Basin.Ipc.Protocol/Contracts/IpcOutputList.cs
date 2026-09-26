namespace Basin.Ipc;

public readonly record struct IpcOutputList(ReadOnlyMemory<IpcOutput> Outputs, string? PointerOutput);
