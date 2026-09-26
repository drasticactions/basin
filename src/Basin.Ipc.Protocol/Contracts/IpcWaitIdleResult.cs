namespace Basin.Ipc;

public readonly record struct IpcWaitIdleResult(long WaitedMs, long Commits, long Ignored);
