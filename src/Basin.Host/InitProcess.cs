using System.Runtime.InteropServices;

using Basin.Diagnostics;

namespace Basin.Host;

/// <summary>
/// Runs a startup command through <c>/bin/sh -c</c> in its own session and process group, with the compositor's socket in its environment.
/// </summary>
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
    private IEventSource? _exitWatch;
    private int _pidfd = -1;

    private InitProcess(int pgid, bool reaped)
    {
        _pgid = pgid;
        Reaped = reaped;
    }

    /// <summary>
    /// Gets the process group id, which is also the id of the shell process.
    /// </summary>
    public int ProcessGroup => _pgid;

    /// <summary>
    /// Gets a value indicating whether the command runs under the basin subreaper.
    /// </summary>
    public bool Reaped { get; }

    /// <summary>
    /// Gets the exit status of the shell process, or <see langword="null"/> before it exits or when no exit watch runs. A process that a signal ended gets 128 plus the signal number.
    /// </summary>
    public int? ExitStatus { get; private set; }

    /// <summary>
    /// Occurs once, on the loop, when the shell process exits. The argument is <see cref="ExitStatus"/>. It requires <see cref="WatchExit"/>.
    /// </summary>
    public event Action<int>? Exited;

    [DllImport("libc", SetLastError = true)]
    private static extern int pidfd_open(int pid, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitid(int idType, int id, byte[] info, int options);

    [DllImport("libc")]
    private static extern int close(int fd);

    /// <summary>
    /// Watches the shell process through a pidfd on the loop, so that <see cref="Exited"/> can occur. A second call does nothing.
    /// </summary>
    /// <param name="loop">The loop that watches the pidfd.</param>
    /// <returns><see langword="false"/> when the pidfd cannot be opened, for example because the process is gone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> is <see langword="null"/>.</exception>
    public bool WatchExit(ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        if (_exitWatch is not null)
        {
            return true;
        }

        _pidfd = pidfd_open(_pgid, 0);
        if (_pidfd < 0)
        {
            return false;
        }

        _exitWatch = loop.AddFd(_pidfd, FdReadiness.Readable, (_, _) => OnExit());
        return true;
    }

    private void OnExit()
    {
        var info = new byte[128];
        if (waitid(3, _pidfd, info, 4 | 1) != 0 || BitConverter.ToInt32(info, 16) == 0)
        {
            return;
        }

        var code = BitConverter.ToInt32(info, 8);
        var status = BitConverter.ToInt32(info, 24);
        ExitStatus = code == 1 ? status : 128 + status;
        _exitWatch?.Remove();
        _exitWatch = null;
        close(_pidfd);
        _pidfd = -1;
        Exited?.Invoke(ExitStatus.Value);
    }

    private const int FileExists = 0;

    private const int FileExecutable = 1;

    private const int ErrorNoEntry = 2;

    private const int ErrorNotDirectory = 20;

    private const int ErrorAccess = 13;

    private const int ErrorNoProcess = 3;

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

    /// <summary>
    /// Looks for an executable <c>init</c> file in each named directory under <c>XDG_CONFIG_HOME</c> or <c>~/.config</c>, in order.
    /// </summary>
    /// <param name="directories">The directory names to search, for example the binary name.</param>
    /// <param name="log">The log for the errors and the paths that were tried.</param>
    /// <returns>The first executable <c>init</c>, nothing, or an <c>init</c> file that exists but is not executable, which stops the search.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directories"/> is <see langword="null"/>.</exception>
    public static InitSearch Find(IReadOnlyList<string> directories, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(directories);

        var root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(root))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home))
            {
                return default;
            }

            root = Path.Combine(home, ".config");
        }

        foreach (var directory in directories)
        {
            var path = Path.Combine(root, directory, "init");
            if (access(path, FileExecutable) == 0)
            {
                return new InitSearch(path, NotExecutable: false);
            }

            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorAccess && access(path, FileExists) == 0)
            {
                log.Error($"init: cannot run {path}, the file is not executable");
                return new InitSearch(null, NotExecutable: true);
            }

            if (error is ErrorNoEntry or ErrorNotDirectory)
            {
                log.Debug($"init: no executable at {path}");
            }
            else
            {
                log.Error($"init: cannot run {path}, {(Marshal.GetPInvokeErrorMessage(error))}");
            }
        }

        return default;
    }

    /// <summary>
    /// Sets the soft open-file limit of the process to 4096, or to the hard limit when that is lower. A soft limit that is already higher stays. <see cref="Start(string, string, string?, BasinLogger, IReadOnlyDictionary{string, string}?, X11Route?, bool)"/> gives the child the original limit.
    /// </summary>
    /// <param name="log">The log for a failure.</param>
    public static void RaiseFileLimit(BasinLogger log) => FileDescriptorLimit.Raise(log);

    /// <summary>
    /// Starts a command without an X11 route and without the subreaper.
    /// </summary>
    /// <param name="command">The shell command.</param>
    /// <param name="socket">The value of <c>WAYLAND_DISPLAY</c> for the command.</param>
    /// <param name="display">The value of <c>DISPLAY</c>, or <see langword="null"/> to keep the inherited one.</param>
    /// <param name="log">The log for the start and for errors.</param>
    /// <param name="environment">Extra variables for the command, or <see langword="null"/>.</param>
    /// <returns>The started process, or <see langword="null"/> when the spawn failed.</returns>
    public static InitProcess? Start(string command, string socket, string? display, BasinLogger log, IReadOnlyDictionary<string, string>? environment = null)
        => Start(command, socket, display, log, environment, null);

    /// <summary>
    /// Starts a command without the subreaper.
    /// </summary>
    /// <param name="command">The shell command.</param>
    /// <param name="socket">The value of <c>WAYLAND_DISPLAY</c> for the command.</param>
    /// <param name="display">The value of <c>DISPLAY</c>, or <see langword="null"/> to keep the inherited one. The X11 route replaces it.</param>
    /// <param name="log">The log for the start and for errors.</param>
    /// <param name="environment">Extra variables for the command, or <see langword="null"/>.</param>
    /// <param name="x11">The X11 display and variables for the command, or <see langword="null"/>.</param>
    /// <returns>The started process, or <see langword="null"/> when the spawn failed.</returns>
    public static InitProcess? Start(
        string command, string socket, string? display, BasinLogger log, IReadOnlyDictionary<string, string>? environment, X11Route? x11)
        => Start(command, socket, display, log, environment, x11, subreaper: false);

    /// <summary>
    /// Starts a command through <c>posix_spawn</c> with a clean signal mask, a default <c>SIGPIPE</c> handler and a new session. The child gets the process environment with the given variables put in.
    /// </summary>
    /// <param name="command">The shell command.</param>
    /// <param name="socket">The value of <c>WAYLAND_DISPLAY</c> for the command.</param>
    /// <param name="display">The value of <c>DISPLAY</c>, or <see langword="null"/> to keep the inherited one. The X11 route replaces it.</param>
    /// <param name="log">The log for the start and for errors.</param>
    /// <param name="environment">Extra variables for the command, or <see langword="null"/>. The X11 route's variables win over them.</param>
    /// <param name="x11">The X11 display and variables for the command, or <see langword="null"/>.</param>
    /// <param name="subreaper">Whether to run the shell under the basin subreaper on Linux. The subreaper adopts orphans and ends the whole tree at exit.</param>
    /// <returns>The started process, or <see langword="null"/> when the spawn failed.</returns>
    public static InitProcess? Start(
        string command,
        string socket,
        string? display,
        BasinLogger log,
        IReadOnlyDictionary<string, string>? environment,
        X11Route? x11,
        bool subreaper)
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

        log.Info($"init: running '{command}'");

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
                log.Error($"init: cannot run the init executable, posix_spawnattr_init failed");
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

            List<string> words = subreaper && OperatingSystem.IsLinux()
                ? [.. ProcessSubreaper.ReaperArgv(["/bin/sh", "-c", command])]
                : ["/bin/sh", "-c", command];
            var program = words[0];
            argv = Block(words);
            envp = Block(EnvironmentBlock(socket, display, environment));

            lowered = FileDescriptorLimit.LowerForSpawn();
            var error = posix_spawn(out var pid, program, IntPtr.Zero, attributes, argv, envp);
            if (error != 0)
            {
                log.Error($"init: cannot run the init executable, {(Marshal.GetPInvokeErrorMessage(error))}");
                return null;
            }

            return new InitProcess(pid, subreaper);
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

    /// <summary>
    /// Sends <c>SIGTERM</c> to the process group and stops the exit watch. <see cref="Exited"/> does not occur after this call. A group with no process left is not a failure.
    /// </summary>
    /// <param name="log">The log for a failure of the signal.</param>
    public void Stop(BasinLogger log)
    {
        if (kill(-_pgid, SignalTerminate) != 0 && Marshal.GetLastPInvokeError() != ErrorNoProcess)
        {
            log.Warn($"init: cannot stop the process group, {(Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError()))}");
        }

        _exitWatch?.Remove();
        _exitWatch = null;
        if (_pidfd >= 0)
        {
            close(_pidfd);
            _pidfd = -1;
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
