using System.Runtime.InteropServices;
using System.Text;
using Wayland.Server;
using static Basin.XWayland.XWaylandLog;

namespace Basin.XWayland;

/// <summary>
/// Reserves an X display and starts Xwayland on it when the first X client connects. When the process exits, the next X client starts it again.
/// </summary>
public sealed unsafe class XWaylandServer : IDisposable
{
    [DllImport("libc", SetLastError = true)]
    private static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    private static extern int bind(int fd, byte* addr, uint addrlen);

    [DllImport("libc", SetLastError = true)]
    private static extern int listen(int fd, int backlog);

    [DllImport("libc", SetLastError = true)]
    private static extern int socketpair(int domain, int type, int protocol, int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern int pipe2(int* fds, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags, int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, byte* buffer, nuint count);

    [DllImport("libc")]
    private static extern int unlink(string path);

    [DllImport("libc", SetLastError = true)]
    private static extern int mkdir(string path, int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawnp(int* pid, string path, nint fileActions, nint attrp, byte** argv, byte** envp);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_init(nint actions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_adddup2(nint actions, int fd, int newFd);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_destroy(nint actions);

    [DllImport("libc", SetLastError = true)]
    private static extern int pidfd_open_syscall(int pid, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern long syscall(long number, long a, long b);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, int* status, int options);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport("libc")]
    private static extern int usleep(uint microseconds);

    private const int AfUnix = 1;
    private const int SockStream = 1;
    private const int OCreat = 0x40;
    private const int OExcl = 0x80;
    private const int OWronly = 1;
    private const int OCloexec = 0x80000;
    private const int Esrch = 3;
    private const int Erofs = 30;
    private const int Sigterm = 15;
    private const int Sigkill = 9;
    private const int WaitNoHang = 1;
    private const long SysPidfdOpen = 434;

    private readonly WlServerDisplay _display;
    private readonly ICompositorEventLoop _loop;
    private readonly int[] _listenFds = [-1, -1];
    private readonly List<IEventSource> _lazySources = [];
    private int _lockFd = -1;
    private int _pid;
    private int _pidFd = -1;
    private IEventSource? _pidSource;
    private IEventSource? _displayFdSource;
    private int _wmFd = -1;
    private Wayland.Server.WlClient? _client;
    private bool _running;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="XWaylandServer"/> class. It takes the first free display in <c>:0</c> to <c>:31</c> and listens on its sockets.
    /// </summary>
    /// <param name="display">The display that the Xwayland client connects to.</param>
    /// <param name="loop">The loop that watches the X sockets and the process.</param>
    /// <exception cref="InvalidOperationException">No display in <c>:0</c> to <c>:31</c> is free.</exception>
    public XWaylandServer(WlServerDisplay display, ICompositorEventLoop loop)
    {
        _display = display;
        _loop = loop;
        DisplayNumber = AllocateDisplay();
        DisplayName = $":{DisplayNumber}";
        WatchLazy();
    }

    /// <summary>
    /// Gets the reserved display number.
    /// </summary>
    public int DisplayNumber { get; }

    /// <summary>
    /// Gets the Wayland client of the running Xwayland process, or <see langword="null"/> when it is not running.
    /// </summary>
    public Wayland.Server.WlClient? Client => _client;

    /// <summary>
    /// Gets the X display name, for example <c>:1</c>, for the <c>DISPLAY</c> variable of X clients.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the number of times the process crashed: it ended on a signal or with a non-zero status. A clean exit at the server reset after the last client, the window manager included, does not count.
    /// </summary>
    public int CrashCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether an Xwayland process is running now.
    /// </summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Occurs when Xwayland reports its display. The argument is the window manager socket fd, and the subscriber takes ownership of it.
    /// </summary>
    public event Action<int>? Ready;

    /// <summary>
    /// Occurs when the Wayland client for a new process is created, before the process starts. A handler can set per-client state before the first request.
    /// </summary>
    public event Action<Wayland.Server.WlClient>? ClientCreated;

    /// <summary>
    /// Occurs when <see cref="X11Scale"/>, <see cref="DpiBuckets"/>, <see cref="CursorTheme"/> or <see cref="CursorSize"/> changes value.
    /// </summary>
    public event Action? SettingsChanged;

    private XWaylandScale _x11Scale;
    private IReadOnlyList<int> _dpiBuckets = [];
    private string _cursorTheme = Environment.GetEnvironmentVariable("XCURSOR_THEME") ?? string.Empty;
    private int _cursorSize =
        int.TryParse(Environment.GetEnvironmentVariable("XCURSOR_SIZE"), out var size) && size > 0 ? size : 24;

    /// <summary>
    /// Gets or sets the scale that X11 clients render at.
    /// </summary>
    public XWaylandScale X11Scale
    {
        get => _x11Scale;
        set
        {
            if (_x11Scale != value)
            {
                _x11Scale = value;
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets the allowed DPI values. The setter copies the list.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public IReadOnlyList<int> DpiBuckets
    {
        get => _dpiBuckets;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_dpiBuckets.SequenceEqual(value))
            {
                _dpiBuckets = [.. value];
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets the cursor theme name. The initial value comes from <c>XCURSOR_THEME</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public string CursorTheme
    {
        get => _cursorTheme;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_cursorTheme != value)
            {
                _cursorTheme = value;
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Gets or sets the cursor size in logical pixels. The initial value comes from <c>XCURSOR_SIZE</c>, or is 24. A value of 0 or less is ignored.
    /// </summary>
    public int CursorSize
    {
        get => _cursorSize;
        set
        {
            if (_cursorSize != value && value > 0)
            {
                _cursorSize = value;
                SettingsChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Occurs after the process exits and the server state is torn down. The display stays reserved.
    /// </summary>
    public event Action? Exited;

    /// <summary>
    /// Stops the process, with SIGKILL when SIGTERM does not work in one second. It then closes the sockets and releases the display.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        StopWatching();
        StopProcess();
        TearDownProcessState();
        foreach (var fd in _listenFds)
        {
            if (fd >= 0)
            {
                close(fd);
            }
        }

        if (_lockFd >= 0)
        {
            close(_lockFd);
            unlink($"/tmp/.X{DisplayNumber}-lock");
            unlink($"/tmp/.X11-unix/X{DisplayNumber}");
        }
    }

    private int AllocateDisplay()
    {
        _ = mkdir("/tmp/.X11-unix", 0x3FF );
        for (var n = 0; n < 32; n++)
        {
            var lockPath = $"/tmp/.X{n}-lock";
            var fd = open(lockPath, OCreat | OExcl | OWronly | OCloexec, 0x1A4 );
            if (fd < 0)
            {
                if (ReclaimStaleLock(lockPath))
                {
                    n--;
                }

                continue;
            }

            var pidText = Encoding.ASCII.GetBytes($"{Environment.ProcessId,10}\n");
            fixed (byte* pidPtr = pidText)
            {
                _ = write(fd, pidPtr, (nuint)pidText.Length);
            }

            if (!TryBindSockets(n))
            {
                close(fd);
                unlink(lockPath);
                continue;
            }

            _lockFd = fd;
            return n;
        }

        throw new InvalidOperationException("No X display from :0 to :31 is free. Stop an X server or remove its stale lock file.");
    }

    private static bool ReclaimStaleLock(string lockPath)
    {
        var fd = open(lockPath, OCloexec , 0);
        if (fd < 0)
        {
            return false;
        }

        Span<byte> text = stackalloc byte[16];
        nint got;
        fixed (byte* ptr = text)
        {
            got = read(fd, ptr, (nuint)text.Length);
        }

        close(fd);
        if (got <= 0 || !int.TryParse(Encoding.ASCII.GetString(text[..(int)got]).Trim(), out var pid) || pid <= 0)
        {
            return false;
        }

        if (kill(pid, 0) == 0 || Marshal.GetLastPInvokeError() != Esrch)
        {
            return false;
        }

        return unlink(lockPath) == 0;
    }

    private bool TryBindSockets(int display)
    {
        var abstractFd = BindUnix($"\0/tmp/.X11-unix/X{display}");
        if (abstractFd < 0)
        {
            return false;
        }

        unlink($"/tmp/.X11-unix/X{display}");
        var pathFd = BindUnix($"/tmp/.X11-unix/X{display}");
        if (pathFd < 0 && Marshal.GetLastPInvokeError() == Erofs)
        {
            Log.Info($"server: /tmp/.X11-unix is read-only, :{display} listens on its abstract socket only");
            _listenFds[0] = abstractFd;
            _listenFds[1] = -1;
            return true;
        }

        if (pathFd < 0)
        {
            close(abstractFd);
            return false;
        }

        _listenFds[0] = abstractFd;
        _listenFds[1] = pathFd;
        return true;
    }

    private static int BindUnix(string path)
    {
        var fd = socket(AfUnix, SockStream | 0x80000 , 0);
        if (fd < 0)
        {
            return -1;
        }

        var addr = stackalloc byte[110];
        addr[0] = AfUnix;
        var bytes = Encoding.UTF8.GetBytes(path);
        for (var i = 0; i < bytes.Length; i++)
        {
            addr[2 + i] = path[0] == '\0' && i == 0 ? (byte)0 : bytes[i];
        }

        var len = (uint)(2 + bytes.Length + (path[0] == '\0' ? 0 : 1));
        if (bind(fd, addr, len) != 0 || listen(fd, 128) != 0)
        {
            close(fd);
            return -1;
        }

        return fd;
    }

    private void WatchLazy()
    {
        foreach (var fd in _listenFds)
        {
            if (fd < 0)
            {
                continue;
            }

            var source = _loop.AddFd(fd, FdReadiness.Readable, (_, _) =>
            {
                if (!_running && !_disposed)
                {
                    Start();
                }
            });
            _lazySources.Add(source);
        }
    }

    private void StopWatching()
    {
        foreach (var source in _lazySources)
        {
            source.Remove();
        }

        _lazySources.Clear();
    }

    private void Start()
    {
        StopWatching();

        var wayland = stackalloc int[2];
        var wm = stackalloc int[2];
        var displayPipe = stackalloc int[2];
        if (socketpair(AfUnix, SockStream, 0, wayland) != 0 ||
            socketpair(AfUnix, SockStream, 0, wm) != 0 ||
            pipe2(displayPipe, 0) != 0)
        {
            throw new InvalidOperationException($"The sockets for Xwayland cannot be made (errno {Marshal.GetLastPInvokeError()}).");
        }

        _client = _display.CreateClient(wayland[0]);
        _client.Destroyed += OnClientGone;
        _wmFd = wm[0];
        ClientCreated?.Invoke(_client);

        var actions = Marshal.AllocHGlobal(80);
        _ = posix_spawn_file_actions_init(actions);
        _ = posix_spawn_file_actions_adddup2(actions, _listenFds[0], 10);
        if (_listenFds[1] >= 0)
        {
            _ = posix_spawn_file_actions_adddup2(actions, _listenFds[1], 11);
        }

        _ = posix_spawn_file_actions_adddup2(actions, displayPipe[1], 12);
        _ = posix_spawn_file_actions_adddup2(actions, wm[1], 13);
        _ = posix_spawn_file_actions_adddup2(actions, wayland[1], 14);

        string[] argv =
        [
            "Xwayland", DisplayName, "-rootless", "-core", "-terminate",
            "-listenfd", "10",
            "-displayfd", "12", "-wm", "13",
        ];
        if (_listenFds[1] >= 0)
        {
            argv = [.. argv[..7], "-listenfd", "11", .. argv[7..]];
        }

        var env = new List<string> { "WAYLAND_SOCKET=14" };
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key != "WAYLAND_SOCKET" && key != "DISPLAY")
            {
                env.Add($"{key}={entry.Value}");
            }
        }

        var argvPtr = MarshalStrings(argv);
        var envPtr = MarshalStrings([.. env]);
        int pid;
        var rc = posix_spawnp(&pid, "Xwayland", actions, 0, argvPtr, envPtr);
        FreeStrings(argvPtr, argv.Length);
        FreeStrings(envPtr, env.Count);
        _ = posix_spawn_file_actions_destroy(actions);
        Marshal.FreeHGlobal(actions);

        close(wayland[1]);
        close(wm[1]);
        close(displayPipe[1]);

        if (rc != 0)
        {
            close(displayPipe[0]);
            throw new InvalidOperationException($"Xwayland cannot start, posix_spawn returned error {rc}. Make sure that Xwayland is on PATH.");
        }

        _pid = pid;
        _running = true;
        Log.Info($"server: Xwayland {DisplayName} started, pid {pid}");

        var readFd = displayPipe[0];
        _displayFdSource = _loop.AddFd(readFd, FdReadiness.Readable, (_, _) =>
        {
            var scratch = stackalloc byte[16];
            _ = read(readFd, scratch, 16);
            _displayFdSource?.Remove();
            _displayFdSource = null;
            close(readFd);
            var wmFd = _wmFd;
            _wmFd = -1;
            Ready?.Invoke(wmFd);
        });

        _pidFd = (int)syscall(SysPidfdOpen, pid, 0);
        if (_pidFd >= 0)
        {
            _pidSource = _loop.AddFd(_pidFd, FdReadiness.Readable, (_, _) => OnExited());
        }
    }

    private void OnClientGone()
    {
        _client = null;
    }

    private void OnExited()
    {
        var status = 0;
        var reaped = waitpid(_pid, &status, 1) == _pid;
        if (reaped && (status & 0x7f) == 0 && ((status >> 8) & 0xff) == 0)
        {
            Log.Info($"server: Xwayland {DisplayName} exited after its last client, it starts again on the next X client");
        }
        else
        {
            Log.Warn($"server: Xwayland {DisplayName} crashed, wait status 0x{status:x}, it starts again on the next X client");
            CrashCount++;
        }

        TearDownProcessState();
        Exited?.Invoke();
        if (!_disposed)
        {
            WatchLazy();
        }
    }

    private void StopProcess()
    {
        if (_pid <= 0)
        {
            return;
        }

        var pid = _pid;
        _ = kill(pid, Sigterm);

        int status;
        for (var i = 0; i < 100; i++)
        {
            if (waitpid(pid, &status, WaitNoHang) != 0)
            {
                return;
            }

            _ = usleep(10_000);
        }

        Log.Warn($"server: Xwayland {DisplayName} ignored SIGTERM, sending SIGKILL");
        _ = kill(pid, Sigkill);
        _ = waitpid(pid, &status, 0);
    }

    private void TearDownProcessState()
    {
        _running = false;
        _pidSource?.Remove();
        _pidSource = null;
        if (_pidFd >= 0)
        {
            close(_pidFd);
            _pidFd = -1;
        }

        _displayFdSource?.Remove();
        _displayFdSource = null;
        if (_wmFd >= 0)
        {
            close(_wmFd);
            _wmFd = -1;
        }

        if (_client is { IsDestroyed: false } client)
        {
            client.Destroy();
        }

        _client = null;
        _pid = 0;
    }

    private static byte** MarshalStrings(string[] values)
    {
        var array = (byte**)Marshal.AllocHGlobal((values.Length + 1) * sizeof(nint));
        for (var i = 0; i < values.Length; i++)
        {
            array[i] = (byte*)Marshal.StringToHGlobalAnsi(values[i]);
        }

        array[values.Length] = null;
        return array;
    }

    private static void FreeStrings(byte** array, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Marshal.FreeHGlobal((nint)array[i]);
        }

        Marshal.FreeHGlobal((nint)array);
    }
}
