using Basin.Diagnostics;
using static Basin.Diagnostics.CoreLog;

namespace Basin;

public sealed class ProcessTracker : IDisposable
{
    private const int SigKill = 9;
    private const int SigTerm = 15;

    private readonly ICompositorEventLoop _loop;
    private readonly Func<IReadOnlyDictionary<string, string?>>? _baseEnvironment;
    private readonly List<TrackedProcess> _processes = [];
    private long _nextLaunch;
    private bool _disposed;

    public ProcessTracker(ICompositorEventLoop loop, Func<IReadOnlyDictionary<string, string?>>? baseEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
        _baseEnvironment = baseEnvironment;
        BasinCounters.Track();
    }

    public IReadOnlyList<TrackedProcess> Processes => _processes;

    public int MaxFinished { get; set; } = 64;

    public Func<X11Density, X11Route?>? X11Routes { get; set; }

    public Func<ProcessLaunch, X11Density?>? X11Default { get; set; }

    public event Action<TrackedProcess>? Exited;

    public TrackedProcess? Spawn(ProcessLaunch launch, out string? error)
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
        var pid = ProcessSpawn.Spawn(argv, Environment(launch), launch.Cwd, launch.LogPath, out error);
        if (pid < 0)
        {
            return null;
        }

        var process = new TrackedProcess(++_nextLaunch, pid, argv, launch.LogPath) { Launch = launch };
        _processes.Add(process);
        Watch(process);
        return process;
    }

    public TrackedProcess? SpawnShell(string command, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        return Spawn(new ProcessLaunch(["/bin/sh", "-c", command]), out error);
    }

    public bool Owns(int pid)
    {
        foreach (var process in _processes)
        {
            if (process.IsRunning && process.Pid == pid)
            {
                return true;
            }
        }

        return false;
    }

    public bool OwnsTree(int pid)
    {
        for (var hops = 0; pid > 1 && hops < 32; hops++)
        {
            if (Owns(pid))
            {
                return true;
            }

            pid = ProcessIdentity.ParentOf(pid);
        }

        return false;
    }

    public static Dictionary<string, string?> SessionEnvironment(
        string? waylandSocket, string? xDisplay, string? basinSocket, IReadOnlyDictionary<string, string>? extra)
    {
        var values = SessionEnvironment(waylandSocket, xDisplay, basinSocket);
        if (extra is not null)
        {
            foreach (var (name, value) in extra)
            {
                values[name] = value;
            }
        }

        return values;
    }

    public static Dictionary<string, string?> SessionEnvironment(string? waylandSocket, string? xDisplay, string? basinSocket)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_SESSION_TYPE"] = "wayland",
            ["DISPLAY"] = xDisplay is { Length: > 0 } ? xDisplay : null,
        };
        if (waylandSocket is { Length: > 0 })
        {
            values["WAYLAND_DISPLAY"] = waylandSocket;
        }

        if (basinSocket is { Length: > 0 })
        {
            values["BASIN_SOCKET"] = basinSocket;
        }

        return values;
    }

    public TrackedProcess? Find(long launchId)
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
            _ = ProcessSpawn.Signal(-process.Pid, SigKill) || (process.IsRunning && ProcessSpawn.Signal(process.Pid, SigKill));
        });
        process.KillTimer.UpdateTimer(Math.Max(1, graceMs));
        return true;
    }

    public void TerminateAll(TimeSpan grace) => Terminate(_processes, grace);

    public void Terminate(TrackedProcess process, TimeSpan grace)
    {
        ArgumentNullException.ThrowIfNull(process);
        Terminate([process], grace);
    }

    private void Terminate(IReadOnlyList<TrackedProcess> processes, TimeSpan grace)
    {
        var running = new List<TrackedProcess>();
        foreach (var process in processes)
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
                    if (!ProcessSpawn.Signal(-running[i].Pid, 0))
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
            _ = ProcessSpawn.Signal(-process.Pid, SigKill);
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

    private static bool Terminate(TrackedProcess process) =>
        ProcessSpawn.Signal(-process.Pid, SigTerm) || (process.IsRunning && ProcessSpawn.Signal(process.Pid, SigTerm));

    private List<string> Environment(ProcessLaunch launch)
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
                if (value is null)
                {
                    values.Remove(name);
                }
                else
                {
                    values[name] = value;
                }
            }
        }

        if ((launch.X11 ?? X11Default?.Invoke(launch)) is { } density && X11Routes?.Invoke(density) is { } route)
        {
            values["DISPLAY"] = route.Display;
            foreach (var (name, value) in route.Environment)
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

    private void Watch(TrackedProcess process)
    {
        var pidfd = ProcessSpawn.PidfdOpenOf(process.Pid);
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

    private bool Reap(TrackedProcess process)
    {
        if (!process.IsRunning)
        {
            return true;
        }

        if (!ProcessSpawn.TryReap(process.Pid, out var status))
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

    private void Unwatch(TrackedProcess process)
    {
        process.Watch?.Remove();
        process.Watch = null;
        process.KillTimer?.Remove();
        process.KillTimer = null;
        if (process.Pidfd >= 0)
        {
            _ = ProcessSpawn.Close(process.Pidfd);
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
