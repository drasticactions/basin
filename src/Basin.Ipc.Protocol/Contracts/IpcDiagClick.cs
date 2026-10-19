namespace Basin.Ipc;

public readonly record struct IpcDiagClick(ulong Window, string Part, double X, double Y, bool Settled);
