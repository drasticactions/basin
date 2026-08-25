using System.Runtime.InteropServices;

using Basin.Diagnostics;

namespace Inlet;

internal sealed class InitProcess
{
    private const int ResourceOpenFiles = 7;

    private const short SpawnSetSigDefault = 0x04;

    private const short SpawnSetSigMask = 0x08;

    private const short SpawnSetSid = 0x80;

    private const int SignalPipe = 13;

    private const int SignalTerminate = 15;

    private const int AttrBytes = 512;

    private const int SigsetBytes = 128;

    private const ulong DesiredOpenFiles = 4096;

    private static RLimit? _originalOpenFiles;

    private readonly int _pgid;

    private InitProcess(int pgid) => _pgid = pgid;

    [StructLayout(LayoutKind.Sequential)]
    private struct RLimit
    {
        public ulong Current;

        public ulong Maximum;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int getrlimit(int resource, out RLimit limit);

    [DllImport("libc", SetLastError = true)]
    private static extern int setrlimit(int resource, in RLimit limit);

    [DllImport("libc")]
    private static extern int posix_spawn(
        out int pid,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr fileActions,
        IntPtr attributes,
        IntPtr[] argv,
        IntPtr[] envp);

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

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    public static void RaiseFileLimit(BasinLogger log)
    {
        if (getrlimit(ResourceOpenFiles, out var original) != 0)
        {
            log.Error($"getrlimit failed, using the system default file descriptor limit");
            return;
        }

        _originalOpenFiles = original;
        var raised = Raised(original);
        if (setrlimit(ResourceOpenFiles, in raised) != 0)
        {
            log.Error($"setrlimit failed, using the system default file descriptor limit of {original.Current}");
            _originalOpenFiles = null;
            return;
        }

        log.Info($"raised the file descriptor limit of the Inlet process to {raised.Current}");
    }

    public static InitProcess? Start(string command, string socket, string? display, BasinLogger log)
    {
        log.Info($"running init executable '{command}'");

        var attributes = Marshal.AllocHGlobal(AttrBytes);
        var mask = Marshal.AllocHGlobal(SigsetBytes);
        var defaults = Marshal.AllocHGlobal(SigsetBytes);
        var argv = Array.Empty<IntPtr>();
        var envp = Array.Empty<IntPtr>();
        var lowered = false;
        var initialized = false;

        try
        {
            if (posix_spawnattr_init(attributes) != 0)
            {
                log.Error($"failed to run the init executable: posix_spawnattr_init failed");
                return null;
            }

            initialized = true;

            sigemptyset(mask);
            sigemptyset(defaults);
            sigaddset(defaults, SignalPipe);
            posix_spawnattr_setsigmask(attributes, mask);
            posix_spawnattr_setsigdefault(attributes, defaults);
            posix_spawnattr_setflags(
                attributes,
                (short)(SpawnSetSid | SpawnSetSigMask | SpawnSetSigDefault));

            argv = Block(["/bin/sh", "-c", command]);
            envp = Block(EnvironmentBlock(socket, display));

            lowered = LowerFileLimit();
            var error = posix_spawn(out var pid, "/bin/sh", IntPtr.Zero, attributes, argv, envp);
            if (error != 0)
            {
                log.Error($"failed to run the init executable: {(Marshal.GetPInvokeErrorMessage(error))}");
                return null;
            }

            return new InitProcess(pid);
        }
        finally
        {
            if (lowered)
            {
                RaiseFileLimitAgain(log);
            }

            if (initialized)
            {
                posix_spawnattr_destroy(attributes);
            }

            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(mask);
            Marshal.FreeHGlobal(defaults);
            Free(argv);
            Free(envp);
        }
    }

    public void Stop(BasinLogger log)
    {
        if (kill(-_pgid, SignalTerminate) != 0)
        {
            log.Error($"failed to stop the init process group: {(Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError()))}");
        }
    }

    private static RLimit Raised(RLimit original) =>
        new() { Current = Math.Min(DesiredOpenFiles, original.Maximum), Maximum = original.Maximum };

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

    private static bool LowerFileLimit() =>
        _originalOpenFiles is { } original && setrlimit(ResourceOpenFiles, in original) == 0;

    private static void RaiseFileLimitAgain(BasinLogger log)
    {
        if (_originalOpenFiles is not { } original)
        {
            return;
        }

        var raised = Raised(original);
        if (setrlimit(ResourceOpenFiles, in raised) != 0)
        {
            log.Error($"failed to raise the file descriptor limit of the Inlet process again");
        }
    }

    private static IntPtr[] Block(List<string> entries)
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
