using System.Runtime.InteropServices;

namespace Basin;

/// <summary>
/// Runs a command under a small reaper process that adopts and stops all its descendants. A compositor uses it so that no client remains after the session ends.
/// </summary>
public static class ProcessSubreaper
{
    /// <summary>
    /// The command-line word that makes a basin program run as the reaper. The program must call <see cref="Run"/> when its first argument is this word.
    /// </summary>
    public const string Mode = "--basin-reaper";

    private const int PrSetPdeathsig = 1;
    private const int PrSetChildSubreaper = 36;
    private const int PEventAll = 0;
    private const int WaitExited = 4;
    private const int WaitNoHang = 1;
    private const int ErrorInterrupted = 4;
    private const int SignalKill = 9;
    private const int SignalTerminate = 15;
    private const int KillGraceMs = 2000;

    [DllImport("libc", SetLastError = true)]
    private static extern int prctl(int option, nuint arg2, nuint arg3, nuint arg4, nuint arg5);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int waitid(int idType, int id, byte* info, int options);

    [DllImport("libc", SetLastError = true)]
    private static extern int getpid();

    [DllImport("libc")]
    private static extern int posix_spawnp(
        out int pid,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string file,
        IntPtr fileActions,
        IntPtr attributes,
        IntPtr[] argv,
        IntPtr[] envp);

    /// <summary>
    /// Makes the current process a child subreaper, so that orphaned descendants become its children.
    /// </summary>
    /// <returns><see langword="false"/> when <c>prctl</c> failed.</returns>
    public static bool Become() => prctl(PrSetChildSubreaper, 1, 0, 0, 0) == 0;

    /// <summary>
    /// Makes the arguments that start this program as a reaper around a command. It throws <see cref="InvalidOperationException"/> when the process path is unknown.
    /// </summary>
    /// <param name="command">The program and the arguments to run under the reaper.</param>
    /// <returns>The full argument list, which starts with the path of this program.</returns>
    public static IReadOnlyList<string> ReaperArgv(IReadOnlyList<string> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var argv = new List<string>();
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("the process path is unknown");
        argv.Add(path);
        if (Path.GetFileNameWithoutExtension(path) == "dotnet" && Environment.GetCommandLineArgs() is [{ } entry, ..])
        {
            argv.Add(entry);
        }

        argv.Add(Mode);
        argv.Add("--");
        argv.AddRange(command);
        return argv;
    }

    /// <summary>
    /// Lists all descendants of a process through <c>/proc/PID/task/TID/children</c>.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>The descendant process IDs, parents before their children.</returns>
    public static List<int> Descendants(int pid)
    {
        var found = new List<int>();
        Collect(pid, found);
        return found;
    }

    /// <summary>
    /// Sends a signal to every descendant of the current process.
    /// </summary>
    /// <param name="signal">The signal number.</param>
    /// <returns>The number of descendants that were found.</returns>
    public static int SignalDescendants(int signal)
    {
        var descendants = Descendants(getpid());
        foreach (var pid in descendants)
        {
            _ = kill(pid, signal);
        }

        return descendants.Count;
    }

    /// <summary>
    /// Runs as the reaper. It starts the command after <c>--</c>, waits for it, then stops the remaining descendants with SIGTERM and, after 2 seconds, SIGKILL.
    /// </summary>
    /// <param name="args">The arguments of the reaper process.</param>
    /// <returns>The exit code of the command, 128 plus the signal number when a signal ended it, or 127 when it did not start.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var separator = Array.IndexOf(args, "--");
        var command = separator >= 0 ? args[(separator + 1)..] : args;
        if (command.Length == 0)
        {
            Console.Error.WriteLine($"{Mode}: no command after --");
            return 1;
        }

        Become();
        _ = prctl(PrSetPdeathsig, SignalTerminate, 0, 0, 0);
        var stopping = 0;
        void Stop(PosixSignalContext context)
        {
            context.Cancel = true;
            if (Interlocked.Exchange(ref stopping, 1) == 0)
            {
                _ = SignalDescendants(SignalTerminate);
            }
        }

        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
        using var hangup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Stop);
        using var quit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, Stop);

        var primary = Spawn(command);
        if (primary < 0)
        {
            Console.Error.WriteLine($"{Mode}: could not start {command[0]}");
            return 127;
        }

        var status = 0;
        while (true)
        {
            var (pid, code) = Wait(blocking: true);
            if (pid < 0)
            {
                return status;
            }

            if (pid == primary)
            {
                status = code;
                break;
            }
        }

        _ = SignalDescendants(SignalTerminate);
        var deadline = Environment.TickCount64 + KillGraceMs;
        while (Descendants(getpid()).Count > 0)
        {
            while (Wait(blocking: false).Pid > 0)
            {
            }

            if (Environment.TickCount64 >= deadline)
            {
                _ = SignalDescendants(SignalKill);
                while (Wait(blocking: true).Pid > 0)
                {
                }

                break;
            }

            Thread.Sleep(20);
        }

        return status;
    }

    private static unsafe (int Pid, int Status) Wait(bool blocking)
    {
        var info = stackalloc byte[128];
        new Span<byte>(info, 128).Clear();
        while (true)
        {
            if (waitid(PEventAll, 0, info, WaitExited | (blocking ? 0 : WaitNoHang)) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == ErrorInterrupted)
                {
                    continue;
                }

                return (-1, 0);
            }

            var pid = *(int*)(info + 16);
            if (pid == 0)
            {
                return (0, 0);
            }

            var code = *(int*)(info + 8);
            var status = *(int*)(info + 24);
            return (pid, code == 1 ? status : 128 + status);
        }
    }

    private static int Spawn(string[] command)
    {
        var argv = new IntPtr[command.Length + 1];
        var environment = Environment.GetEnvironmentVariables();
        var envp = new IntPtr[environment.Count + 1];
        try
        {
            for (var i = 0; i < command.Length; i++)
            {
                argv[i] = Marshal.StringToCoTaskMemUTF8(command[i]);
            }

            var index = 0;
            foreach (System.Collections.DictionaryEntry entry in environment)
            {
                envp[index++] = Marshal.StringToCoTaskMemUTF8($"{entry.Key}={entry.Value}");
            }

            return posix_spawnp(out var pid, command[0], IntPtr.Zero, IntPtr.Zero, argv, envp) == 0 ? pid : -1;
        }
        finally
        {
            foreach (var pointer in argv)
            {
                Marshal.FreeCoTaskMem(pointer);
            }

            foreach (var pointer in envp)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    private static void Collect(int pid, List<int> found)
    {
        string[] tasks;
        try
        {
            tasks = Directory.GetDirectories($"/proc/{pid}/task");
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var task in tasks)
        {
            string text;
            try
            {
                text = File.ReadAllText(Path.Combine(task, "children"));
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(word, out var child) && !found.Contains(child))
                {
                    found.Add(child);
                    Collect(child, found);
                }
            }
        }
    }
}
