using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Config;

/// <summary>
/// Moves an X11 application to another X server by writing a <c>[[launch]]</c> rule and relaunching the process when it exits.
/// </summary>
public sealed class LaunchRuleDensityControl : IX11DensityControl, IDisposable
{
    private readonly Func<IToplevelModel?> _model;
    private readonly ProcessTracker _tracker;
    private readonly ICompositorEventLoop _loop;
    private readonly Func<string?> _configPath;
    private readonly BasinLogger _log;
    private readonly Dictionary<long, Pending> _pending = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchRuleDensityControl"/> class. It subscribes to process exits on <paramref name="tracker"/> until disposal.
    /// </summary>
    /// <param name="model">Gives the toplevel model, or <see langword="null"/> when there is none yet.</param>
    /// <param name="tracker">The tracker that started the processes and relaunches them.</param>
    /// <param name="loop">The loop for the grace timers.</param>
    /// <param name="configPath">Gives the path of the configuration file, or <see langword="null"/> when there is none.</param>
    /// <param name="log">The logger for relaunch messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/>, <paramref name="tracker"/>, <paramref name="loop"/> or <paramref name="configPath"/> is <see langword="null"/>.</exception>
    public LaunchRuleDensityControl(
        Func<IToplevelModel?> model, ProcessTracker tracker, ICompositorEventLoop loop, Func<string?> configPath, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(configPath);
        _model = model;
        _tracker = tracker;
        _loop = loop;
        _configPath = configPath;
        _log = log;
        _tracker.Exited += OnExited;
    }

    /// <summary>
    /// Gets or sets how long the process can take to exit after its windows are asked to close. The default is 5 seconds.
    /// </summary>
    public TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the function that finds a desktop entry ID for an app ID, for a process that the compositor did not start.
    /// </summary>
    public Func<string, string?>? DesktopIdForAppId { get; set; }

    /// <summary>
    /// Occurs after a rule is written, with the rules read back from the file.
    /// </summary>
    public event Action<LaunchRules>? RulesWritten;

    /// <summary>
    /// Writes a rule for the window's application. If the compositor started the process, it closes the process's X11 windows and relaunches it on exit.
    /// The process is never killed. <paramref name="completed"/> reports the outcome, now or after the exit or the grace time.
    /// </summary>
    /// <param name="toplevelId">The ID of the X11 toplevel.</param>
    /// <param name="density">The X server density for the rule.</param>
    /// <param name="completed">Receives the outcome exactly once when the method returns <see langword="true"/>.</param>
    /// <param name="error">The reason when the method returns <see langword="false"/>, else <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the window is unknown or not X11, there is no configuration file, nothing names the program, or the write fails.</returns>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public bool TrySet(ulong toplevelId, X11Density density, Action<X11DensityResult> completed, out string? error)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_model() is not { } model || !model.TryGet(toplevelId, out var info))
        {
            error = $"no window {toplevelId}";
            return false;
        }

        if (info.X11Display is null)
        {
            error = $"window {toplevelId} is not an X11 window";
            return false;
        }

        if (_configPath() is not { Length: > 0 } path)
        {
            error = "the compositor has no configuration file to write a launch rule into";
            return false;
        }

        var pid = (int)info.Pid;
        var identity = ProcessIdentity.Read(pid);
        var known = pid > 0 && !identity.NameMatches("Xwayland");
        var tracked = known ? TrackedOf(pid) : null;
        var desktopId = tracked?.Launch?.DesktopId
            ?? (info.AppId.Length > 0 ? DesktopIdForAppId?.Invoke(info.AppId) : null);
        var exec = tracked?.Launch is { } launch ? LaunchRules.ProgramOf(launch.Argv)
            : identity.Name is { Length: > 0 } name ? name : null;
        if (desktopId is null && exec is null)
        {
            error = $"window {toplevelId} names no desktop entry and no program to write a rule for";
            return false;
        }

        string rule;
        try
        {
            rule = Write(path, desktopId, exec, density);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = $"cannot write {path}: {exception.Message}";
            return false;
        }

        error = null;
        RulesWritten?.Invoke(LaunchRules.Load(path, _log));
        if (tracked is null || tracked.Launch is null)
        {
            completed(new X11DensityResult(X11DensityOutcome.AppliesNextStart, rule,
                known ? "the compositor did not start this process" : "the window reports no process of its own"));
            return true;
        }

        if (_tracker.X11Routes?.Invoke(density) is { } route && route.Display == info.X11Display)
        {
            completed(new X11DensityResult(X11DensityOutcome.Unchanged, rule, $"the window is on {route.Display} already"));
            return true;
        }

        if (_pending.ContainsKey(tracked.LaunchId))
        {
            completed(new X11DensityResult(X11DensityOutcome.StillRunning, rule, "a relaunch of this process is in progress"));
            return true;
        }

        var pending = new Pending(tracked, density, rule, completed);
        _pending[tracked.LaunchId] = pending;
        pending.Timer = _loop.AddTimer(() => GiveUp(pending));
        pending.Timer.UpdateTimer((int)Math.Max(1, Grace.TotalMilliseconds));
        CloseWindowsOf(model, pid, tracked.Pid);
        return true;
    }

    /// <summary>
    /// Stops waiting for exits and completes each pending request with <see cref="X11DensityOutcome.StillRunning"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tracker.Exited -= OnExited;
        foreach (var pending in _pending.Values.ToArray())
        {
            pending.Timer?.Remove();
            pending.Completed(new X11DensityResult(X11DensityOutcome.StillRunning, pending.Rule, "the compositor stopped"));
        }

        _pending.Clear();
    }

    /// <summary>
    /// Writes or changes a <c>[[launch]]</c> rule in a file and keeps every comment and column. The rule is keyed on the desktop ID when there is one, else on the program.
    /// A row with the same key and without the other key is changed. Otherwise a new row is added.
    /// </summary>
    /// <param name="path">The configuration file. It is created when it does not exist.</param>
    /// <param name="desktopId">The desktop entry ID, or <see langword="null"/>.</param>
    /// <param name="exec">The program name. It must not be <see langword="null"/> when <paramref name="desktopId"/> is <see langword="null"/>.</param>
    /// <param name="density">The X server density for the rule.</param>
    /// <returns>A short text form of the rule, for example <c>exec=xfig x11=low-density</c>.</returns>
    /// <exception cref="InvalidOperationException">The file does not parse after the edit.</exception>
    /// <exception cref="IOException">The file cannot be read or written.</exception>
    public static string Write(string path, string? desktopId, string? exec, X11Density density)
    {
        ArgumentNullException.ThrowIfNull(path);
        var (key, value) = desktopId is not null ? ("desktop_id", LaunchRule.NormalizeDesktopId(desktopId)) : ("exec", exec!);
        var document = File.Exists(path) ? TomlDocument.Load(path) : TomlDocument.Parse(string.Empty);
        var literal = TomlValue.From(value).Text;
        var other = key == "desktop_id" ? "exec" : "desktop_id";
        var index = -1;
        for (var i = 0; i < document.TableCount(LaunchRules.Table); i++)
        {
            if (document.RawValueInTable(LaunchRules.Table, i, key) == literal && !document.ContainsInTable(LaunchRules.Table, i, other))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            index = document.AppendTable(LaunchRules.Table);
            document.SetInTable(LaunchRules.Table, index, key, TomlValue.From(value));
        }

        document.SetInTable(LaunchRules.Table, index, "x11", TomlValue.From(LaunchRules.Name(density)));
        document.Save(path);
        return $"{key}={value} x11={LaunchRules.Name(density)}";
    }

    private TrackedProcess? TrackedOf(int pid)
    {
        var group = ProcessIdentity.ProcessGroupOf(pid);
        foreach (var process in _tracker.Processes)
        {
            if (process.IsRunning && (process.Pid == pid || process.Pid == group))
            {
                return process;
            }
        }

        return null;
    }

    private static void CloseWindowsOf(IToplevelModel model, int pid, int group)
    {
        var buffer = new ToplevelInfo[64];
        int count;
        while ((count = model.Enumerate(buffer)) < 0 || count == buffer.Length)
        {
            buffer = new ToplevelInfo[buffer.Length * 4];
        }

        for (var i = 0; i < count; i++)
        {
            var window = buffer[i];
            if (window.X11Display is null || window.Pid == 0)
            {
                continue;
            }

            var windowPid = (int)window.Pid;
            if (windowPid == pid || windowPid == group || ProcessIdentity.ProcessGroupOf(windowPid) == group)
            {
                _ = model.Request(window.Id, new ToplevelRequest(ToplevelRequestKind.Close));
            }
        }
    }

    private void OnExited(TrackedProcess process)
    {
        if (!_pending.Remove(process.LaunchId, out var pending))
        {
            return;
        }

        pending.Timer?.Remove();
        var launch = process.Launch! with { X11 = pending.Density };
        if (_tracker.Spawn(launch, out var error) is { } relaunched)
        {
            _log.Info($"relaunched launch {process.LaunchId} as {relaunched.LaunchId} on the {LaunchRules.Name(pending.Density)} X server");
            pending.Completed(new X11DensityResult(X11DensityOutcome.Relaunched, pending.Rule, null, relaunched.LaunchId));
            return;
        }

        pending.Completed(new X11DensityResult(X11DensityOutcome.AppliesNextStart, pending.Rule, $"the relaunch failed: {error}"));
    }

    private void GiveUp(Pending pending)
    {
        pending.Timer?.Remove();
        pending.Timer = null;
        if (_pending.Remove(pending.Process.LaunchId))
        {
            pending.Completed(new X11DensityResult(X11DensityOutcome.StillRunning, pending.Rule,
                $"pid {pending.Process.Pid} did not exit within {Grace.TotalSeconds:0.#} s; nothing was killed"));
        }
    }

    private sealed class Pending(TrackedProcess process, X11Density density, string rule, Action<X11DensityResult> completed)
    {
        public TrackedProcess Process { get; } = process;

        public X11Density Density { get; } = density;

        public string Rule { get; } = rule;

        public Action<X11DensityResult> Completed { get; } = completed;

        public IEventSource? Timer { get; set; }
    }
}
