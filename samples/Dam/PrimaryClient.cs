using System.Runtime.InteropServices;
using Basin;

using Basin.Diagnostics;

namespace Dam;

internal sealed class PrimaryClient : IDisposable
{
    private const int SignalPipe = 13;

    private const short SpawnSetSigDefault = 0x04;

    private const short SpawnSetSigMask = 0x08;

    private const int FdCloexec = 1;

    private const int SetFd = 2;

    private const int AttrBytes = 512;

    private const int FileActionsBytes = 128;

    private const int SigsetBytes = 128;

    private readonly int _pid;
    private IEventSource? _source;
    private int _readFd = -1;

    private PrimaryClient(int pid, int readFd) => (_pid, _readFd) = (pid, readFd);

    public bool ReturnAppCode { get; private set; }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int pipe(int* fds);

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int arg);

    [DllImport("libc")]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport("libc")]
    private static extern int posix_spawnp(
        out int pid,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string file,
        IntPtr fileActions,
        IntPtr attributes,
        IntPtr[] argv,
        IntPtr[] envp);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_init(IntPtr actions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_destroy(IntPtr actions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_addclose(IntPtr actions, int fd);

    [DllImport("libc")]
    private static extern int posix_spawnattr_init(IntPtr attributes);

    [DllImport("libc")]
    private static extern int posix_spawnattr_destroy(IntPtr attributes);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setflags(IntPtr attributes, short flags);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setsigmask(IntPtr attributes, IntPtr mask);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setsigdefault(IntPtr attributes, IntPtr mask);

    [DllImport("libc")]
    private static extern int sigemptyset(IntPtr set);

    [DllImport("libc")]
    private static extern int sigaddset(IntPtr set, int signal);

    [DllImport("libc")]
    private static extern uint getuid();

    [DllImport("libc")]
    private static extern uint geteuid();

    [DllImport("libc")]
    private static extern uint getgid();

    [DllImport("libc")]
    private static extern uint getegid();

    [DllImport("libc", SetLastError = true)]
    private static extern int setuid(uint uid);

    [DllImport("libc", SetLastError = true)]
    private static extern int setgid(uint gid);

    public static bool DropPermissions(BasinLogger log)
    {
        if (getuid() == 0 || getgid() == 0)
        {
            log.Info($"running as root user, this is dangerous");
            return true;
        }

        if (getuid() != geteuid() || getgid() != getegid())
        {
            log.Info($"setuid/setgid bit detected, dropping permissions");
            if (setgid(getgid()) != 0 || setuid(getuid()) != 0)
            {
                log.Error($"unable to drop root, refusing to start");
                return false;
            }
        }

        if (setgid(0) != -1 || setuid(0) != -1)
        {
            log.Error($"unable to drop root (it can be restored after setuid), refusing to start");
            return false;
        }

        return true;
    }

    public static unsafe PrimaryClient? Spawn(
        string[] application,
        string socket,
        string? display,
        ICompositorEventLoop loop,
        Action onHangup,
        BasinLogger log)
    {
        var fds = stackalloc int[2];
        if (pipe(fds) != 0)
        {
            log.Error($"unable to create pipe");
            return null;
        }

        var readFd = fds[0];
        var writeFd = fds[1];

        var attributes = Marshal.AllocHGlobal(AttrBytes);
        var actions = Marshal.AllocHGlobal(FileActionsBytes);
        var mask = Marshal.AllocHGlobal(SigsetBytes);
        var defaults = Marshal.AllocHGlobal(SigsetBytes);
        var argv = Array.Empty<IntPtr>();
        var envp = Array.Empty<IntPtr>();
        var attributesReady = false;
        var actionsReady = false;

        try
        {
            if (posix_spawnattr_init(attributes) != 0 || !(attributesReady = true) ||
                posix_spawn_file_actions_init(actions) != 0 || !(actionsReady = true))
            {
                log.Error($"failed to spawn the application: posix_spawn setup failed");
                close(readFd);
                close(writeFd);
                return null;
            }

            sigemptyset(mask);
            sigemptyset(defaults);
            sigaddset(defaults, SignalPipe);
            posix_spawnattr_setsigmask(attributes, mask);
            posix_spawnattr_setsigdefault(attributes, defaults);
            posix_spawnattr_setflags(attributes, SpawnSetSigMask | SpawnSetSigDefault);
            posix_spawn_file_actions_addclose(actions, readFd);

            argv = Block(application);
            envp = Block(EnvironmentBlock(socket, display));

            var error = posix_spawnp(out var pid, application[0], actions, attributes, argv, envp);
            if (error != 0)
            {
                log.Error($"failed to spawn '{(application[0])}': {(Marshal.GetPInvokeErrorMessage(error))}");
                close(readFd);
                close(writeFd);
                return null;
            }

            fcntl(readFd, SetFd, FdCloexec);
            fcntl(writeFd, SetFd, FdCloexec);
            close(writeFd);

            var client = new PrimaryClient(pid, readFd);
            client._source = loop.AddFd(readFd, FdReadiness.Hangup | FdReadiness.Error, (_, _) =>
            {
                client.ClosePipe();
                client.ReturnAppCode = true;
                onHangup();
            });

            log.Debug($"application spawned with pid {pid}");
            return client;
        }
        finally
        {
            if (actionsReady)
            {
                posix_spawn_file_actions_destroy(actions);
            }

            if (attributesReady)
            {
                posix_spawnattr_destroy(attributes);
            }

            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(mask);
            Marshal.FreeHGlobal(defaults);
            Free(argv);
            Free(envp);
        }
    }

    public int WaitAndDecode()
    {
        if (waitpid(_pid, out var status, 0) < 0)
        {
            return 0;
        }

        if ((status & 0x7f) == 0)
        {
            return (status >> 8) & 0xff;
        }

        var signal = status & 0x7f;
        if (((signal + 1) >> 1) > 0)
        {
            return 128 + signal;
        }

        return 0;
    }

    public void Dispose() => ClosePipe();

    private void ClosePipe()
    {
        _source?.Remove();
        _source = null;
        if (_readFd >= 0)
        {
            close(_readFd);
            _readFd = -1;
        }
    }

    private static List<string> EnvironmentBlock(string socket, string? display)
    {
        var entries = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (name is "WAYLAND_DISPLAY" || (display is not null && name is "DISPLAY"))
            {
                continue;
            }

            entries.Add($"{name}={entry.Value}");
        }

        entries.Add($"WAYLAND_DISPLAY={socket}");
        if (display is not null)
        {
            entries.Add($"DISPLAY={display}");
        }

        return entries;
    }

    private static IntPtr[] Block(IReadOnlyList<string> entries)
    {
        var block = new IntPtr[entries.Count + 1];
        for (var i = 0; i < entries.Count; i++)
        {
            block[i] = Marshal.StringToCoTaskMemUTF8(entries[i]);
        }

        block[entries.Count] = IntPtr.Zero;
        return block;
    }

    private static void Free(IntPtr[] block)
    {
        foreach (var entry in block)
        {
            if (entry != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(entry);
            }
        }
    }
}
