using Basin.Diagnostics;

namespace Basin.UI.Avalonia;

public sealed class ScreenReader : IDisposable
{
    private readonly ProcessTracker _processes;
    private readonly BasinLogger _log;
    private IReadOnlyList<string> _argv;
    private string _mode;
    private TrackedProcess? _process;
    private bool _treeOnBus;
    private bool _disposed;

    public ScreenReader(ProcessTracker processes, IReadOnlyList<string> argv, string mode, BasinLogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(argv);
        ArgumentNullException.ThrowIfNull(mode);
        _processes = processes;
        _argv = argv;
        _mode = mode;
        _log = log ?? BasinLog.For("reader");
        _processes.Exited += OnExited;
        Unavailable = Reason();
    }

    public string? Unavailable { get; private set; }

    public bool IsAvailable => Unavailable is null;

    public bool IsRunning => _process is { IsRunning: true };

    public TrackedProcess? Process => _process;

    public IReadOnlyList<string> Argv
    {
        get => _argv;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _argv = value;
            Refresh();
        }
    }

    public string Mode
    {
        get => _mode;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _mode = value;
            Refresh();
        }
    }

    public event Action? Changed;

    public event Action<TrackedProcess>? Started;

    public event Action<string>? StartFailed;

    public event Action? Stopped;

    public void AccessibilityStarted(bool treeOnBus)
    {
        _treeOnBus = treeOnBus;
        Refresh();
        if (IsAvailable && _mode == "on")
        {
            Start();
        }
    }

    public void Start()
    {
        if (!IsAvailable || IsRunning)
        {
            Changed?.Invoke();
            return;
        }

        _process = _processes.Spawn(new ProcessLaunch([.. _argv]), out var error);
        if (_process is null)
        {
            _log.Warn($"cannot start {_argv[0]}: {error}");
            Changed?.Invoke();
            StartFailed?.Invoke(error ?? "");
            return;
        }

        Changed?.Invoke();
        Started?.Invoke(_process);
    }

    public void Stop()
    {
        if (_process is not { } process)
        {
            return;
        }

        _process = null;
        _processes.Terminate(process, TimeSpan.FromMilliseconds(500));
        Changed?.Invoke();
        if (!process.IsRunning)
        {
            Stopped?.Invoke();
        }
    }

    public void Toggle()
    {
        if (IsRunning)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _processes.Exited -= OnExited;
    }

    private void Refresh()
    {
        var reason = Reason();
        if (reason is not null && _treeOnBus)
        {
            _log.Info($"no screen reader: {reason}");
        }

        if (reason != Unavailable)
        {
            Unavailable = reason;
            Changed?.Invoke();
        }
    }

    private string? Reason()
    {
        if (_mode == "off")
        {
            return "[accessibility] screen_reader is off";
        }

        if (Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is not { Length: > 0 })
        {
            return "there is no session bus";
        }

        if (!_treeOnBus)
        {
            return "the accessibility tree is not on the bus";
        }

        if (_argv.Count == 0)
        {
            return "no screen reader command";
        }

        return Which(_argv[0]) is null ? _argv[0] + " is not installed" : null;
    }

    private void OnExited(TrackedProcess process)
    {
        if (ReferenceEquals(process, _process))
        {
            _process = null;
            Changed?.Invoke();
            Stopped?.Invoke();
        }
    }

    private static string? Which(string program)
    {
        if (program.Contains('/', StringComparison.Ordinal))
        {
            return File.Exists(program) ? program : null;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
