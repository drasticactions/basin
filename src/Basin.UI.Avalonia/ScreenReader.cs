using Basin.Diagnostics;

namespace Basin.UI.Avalonia;

/// <summary>
/// Starts and stops a screen reader process when the chrome accessibility tree is on the session bus.
/// </summary>
public sealed class ScreenReader : IDisposable
{
    private readonly ProcessTracker _processes;
    private readonly BasinLogger _log;
    private IReadOnlyList<string> _argv;
    private string _mode;
    private TrackedProcess? _process;
    private bool _treeOnBus;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScreenReader"/> class. It does not start the reader.
    /// </summary>
    /// <param name="processes">The tracker that spawns and reaps the reader.</param>
    /// <param name="argv">The reader command and its arguments.</param>
    /// <param name="mode">The mode. <c>"on"</c> starts the reader when the tree reaches the bus, and <c>"off"</c> makes the reader unavailable.</param>
    /// <param name="log">The logger, or <see langword="null"/> for the <c>reader</c> logger.</param>
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

    /// <summary>
    /// Gets the reason that the reader cannot start, or <see langword="null"/> when it can start.
    /// </summary>
    public string? Unavailable { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Unavailable"/> is <see langword="null"/>.
    /// </summary>
    public bool IsAvailable => Unavailable is null;

    /// <summary>
    /// Gets a value indicating whether the reader process runs now.
    /// </summary>
    public bool IsRunning => _process is { IsRunning: true };

    /// <summary>
    /// Gets the reader process, or <see langword="null"/> when no reader was started or it stopped.
    /// </summary>
    public TrackedProcess? Process => _process;

    /// <summary>
    /// Gets or sets the reader command and its arguments. Setting it computes <see cref="Unavailable"/> again but does not restart a running reader.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the mode, for example <c>"on"</c> or <c>"off"</c>. Setting it computes <see cref="Unavailable"/> again but does not start or stop the reader.
    /// </summary>
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

    /// <summary>
    /// Occurs when the availability or the running state can have changed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Occurs after the reader process is spawned.
    /// </summary>
    public event Action<TrackedProcess>? Started;

    /// <summary>
    /// Occurs when the spawn fails, with the error text.
    /// </summary>
    public event Action<string>? StartFailed;

    /// <summary>
    /// Occurs when the reader process has exited.
    /// </summary>
    public event Action? Stopped;

    /// <summary>
    /// Records whether the accessibility tree reached the bus. When the reader is then available and the mode is <c>"on"</c>, it starts the reader.
    /// </summary>
    /// <param name="treeOnBus"><see langword="true"/> when the chrome tree is on the accessibility bus.</param>
    public void AccessibilityStarted(bool treeOnBus)
    {
        _treeOnBus = treeOnBus;
        Refresh();
        if (IsAvailable && _mode == "on")
        {
            Start();
        }
    }

    /// <summary>
    /// Spawns the reader when it is available and not running. It always raises <see cref="Changed"/>.
    /// </summary>
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

    /// <summary>
    /// Terminates the reader and blocks until it exits. The process group gets SIGKILL after 500 ms. It does nothing when no reader was started.
    /// </summary>
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

    /// <summary>
    /// Stops a running reader, or starts one that does not run.
    /// </summary>
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

    /// <summary>
    /// Stops the reader and stops watching for process exits. A second call does nothing.
    /// </summary>
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
