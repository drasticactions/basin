using Basin.Diagnostics;
using static Basin.Diagnostics.CoreLog;

namespace Basin;

/// <summary>
/// Starts child processes, reaps them through pidfds on the event loop, and signals their process groups. It must be used from the event loop thread.
/// </summary>
public sealed class ProcessTracker : IDisposable
{
    private const int SigKill = 9;
    private const int SigTerm = 15;

    private readonly ICompositorEventLoop _loop;
    private readonly Func<IReadOnlyDictionary<string, string?>>? _baseEnvironment;
    private readonly List<TrackedProcess> _processes = [];
    private long _nextLaunch;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessTracker"/> class.
    /// </summary>
    /// <param name="loop">The loop that watches the pidfds and runs the kill timers.</param>
    /// <param name="baseEnvironment">A function that gives the session variables for each spawn. A <see langword="null"/> value removes the variable.</param>
    public ProcessTracker(ICompositorEventLoop loop, Func<IReadOnlyDictionary<string, string?>>? baseEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
        _baseEnvironment = baseEnvironment;
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the running processes and the most recent finished processes, in launch order.
    /// </summary>
    public IReadOnlyList<TrackedProcess> Processes => _processes;

    /// <summary>
    /// Gets or sets the number of finished processes that <see cref="Processes"/> keeps.
    /// </summary>
    public int MaxFinished { get; set; } = 64;

    /// <summary>
    /// Gets or sets the function that gives the X11 display for a density, or <see langword="null"/> when no Xwayland exists.
    /// </summary>
    public Func<X11Density, X11Route?>? X11Routes { get; set; }

    /// <summary>
    /// Gets or sets the function that selects a density for a launch that has none.
    /// </summary>
    public Func<ProcessLaunch, X11Density?>? X11Default { get; set; }

    /// <summary>
    /// Occurs on the event loop thread after a child is reaped. An exception from a handler is logged and does not stop the tracker.
    /// </summary>
    public event Action<TrackedProcess>? Exited;

    /// <summary>
    /// Starts a child process in its own process group, with the compositor environment and the changes that the launch names.
    /// </summary>
    /// <param name="launch">The process to start.</param>
    /// <param name="error">The reason for a failure, or <see langword="null"/> on success.</param>
    /// <returns>The tracked process, or <see langword="null"/> when the arguments are not valid or the spawn failed.</returns>
    public TrackedProcess? Spawn(ProcessLaunch launch, out string? error)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (launch.Argv.Count == 0 || launch.Argv[0].Length == 0)
        {
            error = "'argv' is empty. Give at least the program to start.";
            return null;
        }

        foreach (var path in (ReadOnlySpan<string?>)[launch.Cwd, launch.LogPath])
        {
            if (path is not null && !Path.IsPathRooted(path))
            {
                error = $"'{path}' is not an absolute path. Give an absolute path.";
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

    /// <summary>
    /// Starts <c>/bin/sh -c</c> with a command.
    /// </summary>
    /// <param name="command">The shell command. It must not be empty.</param>
    /// <param name="error">The reason for a failure, or <see langword="null"/> on success.</param>
    /// <returns>The tracked process, or <see langword="null"/> when the spawn failed.</returns>
    public TrackedProcess? SpawnShell(string command, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        return Spawn(new ProcessLaunch(["/bin/sh", "-c", command]), out error);
    }

    /// <summary>
    /// Tests whether a process ID is a running child of this tracker.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <returns><see langword="true"/> for a running tracked child.</returns>
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

    /// <summary>
    /// Tests whether a process ID is a running child of this tracker or a descendant of one. It follows at most 32 parents through <c>/proc</c>.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <returns><see langword="true"/> when a tracked child is the process or one of its ancestors.</returns>
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

    /// <summary>
    /// Makes the session variables for children, then adds more variables.
    /// </summary>
    /// <param name="waylandSocket">The value for <c>WAYLAND_DISPLAY</c>, or <see langword="null"/> to not set it.</param>
    /// <param name="xDisplay">The value for <c>DISPLAY</c>. <see langword="null"/> or empty removes it.</param>
    /// <param name="basinSocket">The value for <c>BASIN_SOCKET</c>, or <see langword="null"/> to not set it.</param>
    /// <param name="extra">More variables, which override the others.</param>
    /// <returns>A new dictionary where a <see langword="null"/> value means remove the variable.</returns>
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

    /// <summary>
    /// Makes the session variables for children. <c>XDG_SESSION_TYPE</c> is always <c>wayland</c>.
    /// </summary>
    /// <param name="waylandSocket">The value for <c>WAYLAND_DISPLAY</c>, or <see langword="null"/> to not set it.</param>
    /// <param name="xDisplay">The value for <c>DISPLAY</c>. <see langword="null"/> or empty removes it.</param>
    /// <param name="basinSocket">The value for <c>BASIN_SOCKET</c>, or <see langword="null"/> to not set it.</param>
    /// <returns>A new dictionary where a <see langword="null"/> value means remove the variable.</returns>
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

    /// <summary>
    /// Finds a tracked process by launch ID.
    /// </summary>
    /// <param name="launchId">The launch ID.</param>
    /// <returns>The process, or <see langword="null"/> when it is unknown or was pruned.</returns>
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

    /// <summary>
    /// Sends SIGTERM to the process group of a launch, then SIGKILL after a grace time. The call does not block.
    /// </summary>
    /// <param name="launchId">The launch ID.</param>
    /// <param name="graceMs">The time in milliseconds before SIGKILL.</param>
    /// <returns><see langword="false"/> when the launch is unknown or the signal failed.</returns>
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

    /// <summary>
    /// Sends SIGTERM to every tracked process group and blocks until they exit. Groups that remain after the grace time get SIGKILL. Use it at shutdown only.
    /// </summary>
    /// <param name="grace">The maximum wait before SIGKILL.</param>
    public void TerminateAll(TimeSpan grace) => Terminate(_processes, grace);

    /// <summary>
    /// Sends SIGTERM to the process group of one process and blocks until it exits. The group gets SIGKILL when it remains after the grace time.
    /// </summary>
    /// <param name="process">The process to stop.</param>
    /// <param name="grace">The maximum wait before SIGKILL.</param>
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

    /// <summary>
    /// Stops the pidfd watches and the kill timers. The children keep running.
    /// </summary>
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
            Log.Debug($"process: pidfd_open({process.Pid}) failed, launch {process.LaunchId} is reaped only when it is killed");
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
        Log.Debug($"process: launch {process.LaunchId} (pid {process.Pid}) exited");
        Prune();
        try
        {
            Exited?.Invoke(process);
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"process: an exit observer failed");
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
