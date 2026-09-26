using Basin.Diagnostics;
using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

public sealed class IpcProcessTracker : IDisposable
{
    private const int SigKill = 9;
    private const int SigTerm = 15;

    private readonly ICompositorEventLoop _loop;
    private readonly Func<IReadOnlyDictionary<string, string>>? _baseEnvironment;
    private readonly List<IpcTrackedProcess> _processes = [];
    private long _nextLaunch;
    private bool _disposed;

    public IpcProcessTracker(ICompositorEventLoop loop, Func<IReadOnlyDictionary<string, string>>? baseEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
        _baseEnvironment = baseEnvironment;
        BasinCounters.Track();
    }

    public IReadOnlyList<IpcTrackedProcess> Processes => _processes;

    public int MaxFinished { get; set; } = 64;

    public event Action<IpcTrackedProcess>? Exited;

    public IpcTrackedProcess? Spawn(IpcLaunch launch, out string? error)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (launch.Argv.Count == 0 || launch.Argv[0].Length == 0)
        {
            error = "'argv' names at least a program";
            return null;
        }

        foreach (var path in (ReadOnlySpan<string?>)[launch.Cwd, launch.LogPath])
        {
            if (path is not null && !Path.IsPathRooted(path))
            {
                error = $"'{path}' is not absolute";
                return null;
            }
        }

        string[] argv = [.. launch.Argv];
        var pid = IpcSpawn.Spawn(argv, Environment(launch), launch.Cwd, launch.LogPath, out error);
        if (pid < 0)
        {
            return null;
        }

        var process = new IpcTrackedProcess(++_nextLaunch, pid, argv, launch.LogPath);
        _processes.Add(process);
        Watch(process);
        return process;
    }

    public IpcTrackedProcess? Find(long launchId)
    {
        foreach (var process in _processes)
        {
            if (process.LaunchId == launchId)
            {
                return process;
            }
        }

        return null;
    }

    public bool Kill(long launchId, int graceMs = 2000)
    {
        if (Find(launchId) is not { } process)
        {
            return false;
        }

        if (!Terminate(process))
        {
            return false;
        }

        process.KillTimer?.Remove();
        process.KillTimer = _loop.AddTimer(() =>
        {
            process.KillTimer?.Remove();
            process.KillTimer = null;
            _ = IpcSpawn.Signal(-process.Pid, SigKill) || (process.IsRunning && IpcSpawn.Signal(process.Pid, SigKill));
        });
        process.KillTimer.UpdateTimer(Math.Max(1, graceMs));
        return true;
    }

    public void TerminateAll(TimeSpan grace)
    {
        var running = new List<IpcTrackedProcess>();
        foreach (var process in _processes)
        {
            if (Terminate(process))
            {
                running.Add(process);
            }
        }

        var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(grace.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
        while (running.Count > 0 && System.Diagnostics.Stopwatch.GetTimestamp() < deadline)
        {
            for (var i = running.Count - 1; i >= 0; i--)
            {
                if (!running[i].IsRunning || Reap(running[i]))
                {
                    if (!IpcSpawn.Signal(-running[i].Pid, 0))
                    {
                        running.RemoveAt(i);
                    }
                }
            }

            if (running.Count > 0)
            {
                Thread.Sleep(10);
            }
        }

        foreach (var process in running)
        {
            _ = IpcSpawn.Signal(-process.Pid, SigKill);
            _ = Reap(process);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var process in _processes)
        {
            Unwatch(process);
        }

        BasinCounters.Untrack();
    }

    private static bool Terminate(IpcTrackedProcess process) =>
        IpcSpawn.Signal(-process.Pid, SigTerm) || (process.IsRunning && IpcSpawn.Signal(process.Pid, SigTerm));

    private List<string> Environment(IpcLaunch launch)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            values[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        if (_baseEnvironment?.Invoke() is { } overrides)
        {
            foreach (var (name, value) in overrides)
            {
                values[name] = value;
            }
        }

        if (launch.Env is { } env)
        {
            foreach (var (name, value) in env)
            {
                values[name] = value;
            }
        }

        if (launch.UnsetEnv is { } unset)
        {
            foreach (var name in unset)
            {
                values.Remove(name);
            }
        }

        var entries = new List<string>(values.Count);
        foreach (var (name, value) in values)
        {
            entries.Add($"{name}={value}");
        }

        return entries;
    }

    private void Watch(IpcTrackedProcess process)
    {
        var pidfd = IpcSpawn.PidfdOpenOf(process.Pid);
        if (pidfd < 0)
        {
            Log.Debug($"pidfd_open({process.Pid}) failed; launch {process.LaunchId} is reaped only when it is killed");
            return;
        }

        process.Pidfd = pidfd;
        process.Watch = _loop.AddFd(pidfd, FdReadiness.Readable, (_, _) =>
        {
            if (Reap(process))
            {
                Unwatch(process);
            }
        });
    }

    private bool Reap(IpcTrackedProcess process)
    {
        if (!process.IsRunning)
        {
            return true;
        }

        if (!IpcSpawn.TryReap(process.Pid, out var status))
        {
            return false;
        }

        process.Exit(status);
        Log.Debug($"launch {process.LaunchId} (pid {process.Pid}) exited");
        Prune();
        try
        {
            Exited?.Invoke(process);
        }
        catch (Exception exception)
        {
            Log.Error($"an exit observer failed: {exception}");
        }

        return true;
    }

    private void Unwatch(IpcTrackedProcess process)
    {
        process.Watch?.Remove();
        process.Watch = null;
        process.KillTimer?.Remove();
        process.KillTimer = null;
        if (process.Pidfd >= 0)
        {
            _ = UnixSocket.Close(process.Pidfd);
            process.Pidfd = -1;
        }
    }

    private void Prune()
    {
        var finished = 0;
        for (var i = _processes.Count - 1; i >= 0; i--)
        {
            if (_processes[i].IsRunning)
            {
                continue;
            }

            if (++finished > MaxFinished)
            {
                Unwatch(_processes[i]);
                _processes.RemoveAt(i);
            }
        }
    }
}
