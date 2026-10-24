namespace Basin.Ipc;

/// <summary>
/// The data of the <c>process/exited</c> event, which goes out when the tracker reaps a launched process.
/// </summary>
/// <param name="LaunchId">The launch id.</param>
/// <param name="Pid">The process id.</param>
/// <param name="ExitCode">The exit status, or <see langword="null"/> when a signal ended the process.</param>
/// <param name="Signal">The signal that ended the process, or <see langword="null"/> when it exited.</param>
public readonly record struct IpcProcessExited(long LaunchId, int Pid, int? ExitCode, int? Signal);
