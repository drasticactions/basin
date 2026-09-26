using System.Runtime.InteropServices;
using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

internal static unsafe class IpcSpawn
{
    private const short SpawnSetSigDefault = 0x04;
    private const short SpawnSetSigMask = 0x08;
    private const short SpawnSetSid = 0x80;
    private const int SignalPipe = 13;
    private const int AttrBytes = 512;
    private const int ActionBytes = 256;
    private const int SigsetBytes = 128;
    private const long PidfdOpen = 434;
    private const int WaitNoHang = 1;

    public static int Spawn(
        IpcServer server, string[] argv, IReadOnlyDictionary<string, string>? env, string? cwd, out string? error)
    {
        var attributes = Marshal.AllocHGlobal(AttrBytes);
        var actions = Marshal.AllocHGlobal(ActionBytes);
        var mask = Marshal.AllocHGlobal(SigsetBytes);
        var defaults = Marshal.AllocHGlobal(SigsetBytes);
        var argvBlock = Block(argv);
        var envBlock = Block(Environment(server, env));
        try
        {
            _ = posix_spawnattr_init(attributes);
            _ = posix_spawn_file_actions_init(actions);
            _ = sigemptyset(mask);
            _ = sigemptyset(defaults);
            _ = sigaddset(defaults, SignalPipe);
            _ = posix_spawnattr_setsigmask(attributes, mask);
            _ = posix_spawnattr_setsigdefault(attributes, defaults);
            _ = posix_spawnattr_setflags(attributes, (short)(SpawnSetSid | SpawnSetSigMask | SpawnSetSigDefault));
            if (cwd is not null && posix_spawn_file_actions_addchdir_np(actions, cwd) != 0)
            {
                error = $"cannot change to '{cwd}'";
                return -1;
            }

            var result = posix_spawnp(out var pid, argv[0], actions, attributes, argvBlock, envBlock);
            if (result != 0)
            {
                error = $"{argv[0]}: {Marshal.GetPInvokeErrorMessage(result)}";
                return -1;
            }

            Reap(server, pid);
            error = null;
            return pid;
        }
        finally
        {
            _ = posix_spawn_file_actions_destroy(actions);
            _ = posix_spawnattr_destroy(attributes);
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(mask);
            Marshal.FreeHGlobal(defaults);
            Free(argvBlock);
            Free(envBlock);
        }
    }

    private static void Reap(IpcServer server, int pid)
    {
        var pidfd = (int)syscall(PidfdOpen, pid, 0);
        if (pidfd < 0)
        {
            Log.Debug($"pidfd_open({pid}) failed; the child is reaped only when the compositor exits");
            return;
        }

        var reaper = new IpcReaper(pid, pidfd);
        reaper.Source = server.Loop.AddFd(pidfd, FdReadiness.Readable, (_, _) =>
        {
            int status;
            _ = waitpid(pid, &status, WaitNoHang);
            reaper.Dispose();
            server.Disown(reaper);
        });
        server.Own(reaper);
    }

    private static List<string> Environment(IpcServer server, IReadOnlyDictionary<string, string>? env)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            values[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        if (server.Session.WaylandSocket is { Length: > 0 } socket)
        {
            values["WAYLAND_DISPLAY"] = socket;
        }

        if (server.Session.XwaylandDisplay?.Invoke() is { Length: > 0 } display)
        {
            values["DISPLAY"] = display;
        }

        if (server.Path is { } path)
        {
            values[IpcProtocol.SocketVariable] = path;
        }

        if (env is not null)
        {
            foreach (var (name, value) in env)
            {
                values[name] = value;
            }
        }

        var entries = new List<string>(values.Count);
        foreach (var (name, value) in values)
        {
            entries.Add($"{name}={value}");
        }

        return entries;
    }

    private static nint[] Block(IReadOnlyList<string> entries)
    {
        var block = new nint[entries.Count + 1];
        for (var i = 0; i < entries.Count; i++)
        {
            block[i] = Marshal.StringToCoTaskMemUTF8(entries[i]);
        }

        return block;
    }

    private static void Free(nint[] block)
    {
        foreach (var entry in block)
        {
            if (entry != 0)
            {
                Marshal.FreeCoTaskMem(entry);
            }
        }
    }

    [DllImport("libc")]
    private static extern int posix_spawnp(
        out int pid, [MarshalAs(UnmanagedType.LPUTF8Str)] string file, nint actions, nint attributes, nint[] argv, nint[] envp);

    [DllImport("libc")]
    private static extern int posix_spawnattr_init(nint attributes);

    [DllImport("libc")]
    private static extern int posix_spawnattr_destroy(nint attributes);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setflags(nint attributes, short flags);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setsigmask(nint attributes, nint mask);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setsigdefault(nint attributes, nint mask);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_init(nint actions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_destroy(nint actions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_addchdir_np(nint actions, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport("libc")]
    private static extern int sigemptyset(nint set);

    [DllImport("libc")]
    private static extern int sigaddset(nint set, int signal);

    [DllImport("libc", SetLastError = true)]
    private static extern long syscall(long number, long pid, long flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, int* status, int options);
}
