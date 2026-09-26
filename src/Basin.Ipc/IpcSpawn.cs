using System.Runtime.InteropServices;

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

    private const int OWrite = 0x1;
    private const int OCreate = 0x40;
    private const int OAppend = 0x400;

    public static int Spawn(string[] argv, List<string> environment, string? cwd, string? log, out string? error)
    {
        var attributes = Marshal.AllocHGlobal(AttrBytes);
        var actions = Marshal.AllocHGlobal(ActionBytes);
        var mask = Marshal.AllocHGlobal(SigsetBytes);
        var defaults = Marshal.AllocHGlobal(SigsetBytes);
        var argvBlock = Block(argv);
        var envBlock = Block(environment);
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

            if (log is not null
                && (posix_spawn_file_actions_addopen(actions, 1, log, OWrite | OCreate | OAppend, Convert.ToUInt32("644", 8)) != 0
                    || posix_spawn_file_actions_adddup2(actions, 1, 2) != 0))
            {
                error = $"cannot send output to '{log}'";
                return -1;
            }

            var result = posix_spawnp(out var pid, argv[0], actions, attributes, argvBlock, envBlock);
            if (result != 0)
            {
                error = $"{argv[0]}: {Marshal.GetPInvokeErrorMessage(result)}";
                return -1;
            }

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
    private static extern int posix_spawn_file_actions_addopen(
        nint actions, int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_adddup2(nint actions, int fd, int target);

    [DllImport("libc")]
    private static extern int sigemptyset(nint set);

    [DllImport("libc")]
    private static extern int sigaddset(nint set, int signal);

    [DllImport("libc", SetLastError = true)]
    private static extern long syscall(long number, long pid, long flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, int* status, int options);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int KillRaw(int pid, int signal);

    public static int PidfdOpenOf(int pid) => (int)syscall(PidfdOpen, pid, 0);

    public static bool TryReap(int pid, out int status)
    {
        var raw = 0;
        var reaped = waitpid(pid, &raw, WaitNoHang);
        status = raw;
        return reaped == pid;
    }

    public static bool Signal(int pid, int signal) => KillRaw(pid, signal) == 0;
}
