namespace Basin.Ipc;

public readonly record struct IpcProcessExited(long LaunchId, int Pid, int? ExitCode, int? Signal);
