using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Config;

public sealed class LaunchRuleDensityControl : IX11DensityControl, IDisposable
{
    private readonly Func<IToplevelModel?> _model;
    private readonly ProcessTracker _tracker;
    private readonly ICompositorEventLoop _loop;
    private readonly Func<string?> _configPath;
    private readonly BasinLogger _log;
    private readonly Dictionary<long, Pending> _pending = [];
    private bool _disposed;

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

    public TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(5);

    public Func<string, string?>? DesktopIdForAppId { get; set; }

    public event Action<LaunchRules>? RulesWritten;

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
