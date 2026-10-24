namespace Basin.Ipc;

/// <summary>
/// One tracked launch in the reply of <c>process/list</c>.
/// </summary>
/// <param name="LaunchId">The launch id that <c>process/spawn</c> returned. It is unique for the life of the compositor.</param>
/// <param name="Pid">The process id. The process leads its own session, so it is also the process group id.</param>
/// <param name="Argv">The argument vector, program first.</param>
/// <param name="Running"><see langword="true"/> until the tracker reaps the process.</param>
/// <param name="ExitCode">The exit status, or <see langword="null"/> while the process runs or when a signal ended it.</param>
/// <param name="Signal">The signal that ended the process, or <see langword="null"/> while it runs or when it exited.</param>
/// <param name="Log">The absolute path of the log file, or <see langword="null"/> when the launch has no log.</param>
public readonly record struct IpcProcess(
    long LaunchId,
    int Pid,
    ReadOnlyMemory<string> Argv,
    bool Running,
    int? ExitCode,
    int? Signal,
    string? Log);
