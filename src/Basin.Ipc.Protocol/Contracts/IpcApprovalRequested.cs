namespace Basin.Ipc;

public readonly record struct IpcApprovalRequested(long Id, string Method, IpcRawJson Arguments, string Reason);
