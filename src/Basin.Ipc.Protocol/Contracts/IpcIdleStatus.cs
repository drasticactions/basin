namespace Basin.Ipc;

public readonly record struct IpcIdleStatus(long IdleMs, bool Inhibited);
