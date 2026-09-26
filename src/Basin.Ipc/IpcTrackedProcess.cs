namespace Basin.Ipc;

public sealed class IpcTrackedProcess
{
    internal IpcTrackedProcess(long launchId, int pid, string[] argv, string? logPath)
    {
        LaunchId = launchId;
        Pid = pid;
        Argv = argv;
        LogPath = logPath;
    }

    public long LaunchId { get; }

    public int Pid { get; }

    public IReadOnlyList<string> Argv { get; }

    public string? LogPath { get; }

    public bool IsRunning { get; private set; } = true;

    public int? ExitCode { get; private set; }

    public int? Signal { get; private set; }

    internal string[] ArgvArray => (string[])Argv;

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
