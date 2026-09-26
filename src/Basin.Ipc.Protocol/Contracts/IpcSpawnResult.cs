namespace Basin.Ipc;

public readonly record struct IpcSpawnResult(int Pid, long LaunchId = 0);
