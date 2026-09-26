namespace Basin.Ipc;

public readonly record struct IpcProcess(
    long LaunchId,
    int Pid,
    ReadOnlyMemory<string> Argv,
    bool Running,
    int? ExitCode,
    int? Signal,
    string? Log);
