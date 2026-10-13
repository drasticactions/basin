using System.Runtime.InteropServices;

using Basin.Diagnostics;

namespace Basin.Host;

public sealed class InitProcess
{
    private const short SpawnSetSigDefault = 0x04;

    private const short SpawnSetSigMask = 0x08;

    private const short SpawnSetSid = 0x80;

    private const int SignalPipe = 13;

    private const int SignalTerminate = 15;

    private const int AttrBytes = 512;

    private const int SigsetBytes = 128;

    private readonly int _pgid;

    private InitProcess(int pgid) => _pgid = pgid;

    public int ProcessGroup => _pgid;

    private const int FileExists = 0;

    private const int FileExecutable = 1;

    private const int ErrorNoEntry = 2;

    private const int ErrorNotDirectory = 20;

    private const int ErrorAccess = 13;

    [DllImport("libc", SetLastError = true)]
    private static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

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

    public static bool TryFind(IReadOnlyList<string> directories, BasinLogger log, out string? found)
    {
        ArgumentNullException.ThrowIfNull(directories);
        found = null;

        var root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(root))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home))
            {
                return true;
            }

            root = Path.Combine(home, ".config");
        }

        foreach (var directory in directories)
        {
            var path = Path.Combine(root, directory, "init");
            if (access(path, FileExecutable) == 0)
            {
                found = path;
                return true;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorAccess && access(path, FileExists) == 0)
            {
                log.Error($"failed to run init executable {path}: the file is not executable");
                return false;
            }

            if (error is ErrorNoEntry or ErrorNotDirectory)
            {
                log.Debug($"no init executable at {path}");
            }
            else
            {
                log.Error($"failed to run init executable {path}: {(Marshal.GetPInvokeErrorMessage(error))}");
            }
        }

        return true;
    }

    public static void RaiseFileLimit(BasinLogger log) => FileDescriptorLimit.Raise(log);

    public static InitProcess? Start(string command, string socket, string? display, BasinLogger log, IReadOnlyDictionary<string, string>? environment = null)
        => Start(command, socket, display, log, environment, null);

    public static InitProcess? Start(
        string command, string socket, string? display, BasinLogger log, IReadOnlyDictionary<string, string>? environment, X11Route? x11)
    {
        if (x11 is not null)
        {
            display = x11.Display;
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            if (environment is not null)
            {
                foreach (var (name, value) in environment)
                {
                    merged[name] = value;
                }
            }

            foreach (var (name, value) in x11.Environment)
            {
                merged[name] = value;
            }

            environment = merged;
        }

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
            envp = Block(EnvironmentBlock(socket, display, environment));

            lowered = FileDescriptorLimit.LowerForSpawn();
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
                FileDescriptorLimit.RaiseAfterSpawn(log);
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

    private static List<string> EnvironmentBlock(string socket, string? display, IReadOnlyDictionary<string, string>? environment)
    {
        var entries = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (name is "WAYLAND_DISPLAY" || (display is not null && name is "DISPLAY") || (environment?.ContainsKey(name) ?? false))
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

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                if (name is not ("WAYLAND_DISPLAY" or "DISPLAY"))
                {
                    entries.Add($"{name}={value}");
                }
            }
        }

        return entries;
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
