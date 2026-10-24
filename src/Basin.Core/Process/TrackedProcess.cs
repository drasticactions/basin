namespace Basin;

/// <summary>
/// A child process that a <see cref="ProcessTracker"/> started. Its state changes only on the event loop thread.
/// </summary>
public sealed class TrackedProcess
{
    internal TrackedProcess(long launchId, int pid, string[] argv, string? logPath)
    {
        LaunchId = launchId;
        Pid = pid;
        Argv = argv;
        LogPath = logPath;
    }

    /// <summary>
    /// Gets the ID that the tracker gave the launch. IDs start at 1 and are never reused.
    /// </summary>
    public long LaunchId { get; }

    /// <summary>
    /// Gets the process ID. The child is also the leader of its own process group.
    /// </summary>
    public int Pid { get; }

    /// <summary>
    /// Gets the program and the arguments that started the process.
    /// </summary>
    public IReadOnlyList<string> Argv { get; }

    /// <summary>
    /// Gets the file that receives the output of the process, or <see langword="null"/>.
    /// </summary>
    public string? LogPath { get; }

    /// <summary>
    /// Gets the launch description that started the process.
    /// </summary>
    public ProcessLaunch? Launch { get; internal init; }

    /// <summary>
    /// Gets a value indicating whether the tracker did not reap the process yet.
    /// </summary>
    public bool IsRunning { get; private set; } = true;

    /// <summary>
    /// Gets the exit code, or <see langword="null"/> while the process runs or when a signal ended it.
    /// </summary>
    public int? ExitCode { get; private set; }

    /// <summary>
    /// Gets the number of the signal that ended the process, or <see langword="null"/> when it did not end from a signal.
    /// </summary>
    public int? Signal { get; private set; }

    internal IEventSource? Watch { get; set; }

    internal int Pidfd { get; set; } = -1;

    internal IEventSource? KillTimer { get; set; }

    internal void Exit(int status)
    {
        IsRunning = false;
        if ((status & 0x7f) == 0)
        {
            ExitCode = (status >> 8) & 0xff;
        }
        else
        {
            Signal = status & 0x7f;
        }
    }
}
