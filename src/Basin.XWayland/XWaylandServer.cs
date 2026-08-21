using System.Runtime.InteropServices;
using System.Text;
using Wayland.Server;

namespace Basin.XWayland;

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

    public XWaylandServer(WlServerDisplay display, ICompositorEventLoop loop)
    {
        _display = display;
        _loop = loop;
        DisplayNumber = AllocateDisplay();
        DisplayName = $":{DisplayNumber}";
        WatchLazy();
    }

    public int DisplayNumber { get; }

    public Wayland.Server.WlClient? Client => _client;

    public string DisplayName { get; }

    public int CrashCount { get; private set; }

    public bool IsRunning => _running;

    public event Action<int>? Ready;

    public event Action? Exited;

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

        throw new InvalidOperationException("no free X display in :0..:31");
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
            throw new InvalidOperationException("XWayland fd setup failed");
        }

        _client = _display.CreateClient(wayland[0]);
        _client.Destroyed += OnClientGone;
        _wmFd = wm[0];

        var actions = Marshal.AllocHGlobal(80);
        _ = posix_spawn_file_actions_init(actions);
        _ = posix_spawn_file_actions_adddup2(actions, _listenFds[0], 10);
        _ = posix_spawn_file_actions_adddup2(actions, _listenFds[1], 11);
        _ = posix_spawn_file_actions_adddup2(actions, displayPipe[1], 12);
        _ = posix_spawn_file_actions_adddup2(actions, wm[1], 13);
        _ = posix_spawn_file_actions_adddup2(actions, wayland[1], 14);

        string[] argv =
        [
            "Xwayland", DisplayName, "-rootless", "-core", "-terminate",
            "-listenfd", "10", "-listenfd", "11",
            "-displayfd", "12", "-wm", "13",
        ];
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
            throw new InvalidOperationException($"posix_spawn(Xwayland) failed: {rc}");
        }

        _pid = pid;
        _running = true;
        Basin.Diagnostics.BasinLog.Info($"Xwayland {DisplayName} spawned (pid {pid})");

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
        int status;
        _ = waitpid(_pid, &status, 1 );
        Basin.Diagnostics.BasinLog.Warn($"Xwayland {DisplayName} exited; relaunching on next X client");
        TearDownProcessState();
        CrashCount++;
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

        Basin.Diagnostics.BasinLog.Warn($"Xwayland {DisplayName} ignored SIGTERM; killing");
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
