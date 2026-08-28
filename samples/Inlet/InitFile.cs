using System.Runtime.InteropServices;

using Basin.Diagnostics;

namespace Inlet;

internal static class InitFile
{
    private const int FileExists = 0;

    private const int FileExecutable = 1;

    private const int ErrorNoEntry = 2;

    private const int ErrorNotDirectory = 20;

    private const int ErrorAccess = 13;

    [DllImport("libc", SetLastError = true)]
    private static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    public static bool TryResolve(string? command, BasinLogger log, out string? startup)
    {
        startup = null;

        if (command is not null)
        {
            startup = command;
        }
        else if (!TrySearch(log, out startup))
        {
            return false;
        }

        if (startup is null)
        {
            log.Info($"no init executable, running with no window manager");
            return true;
        }

        return !MentionsRiverctl(startup, log);
    }

    private static bool TrySearch(BasinLogger log, out string? found)
    {
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

        string[] directories = ["inlet", "river"];
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

    private static bool MentionsRiverctl(string command, BasinLogger log)
    {
        if (!command.Contains('/'))
        {
            return false;
        }

        try
        {
            if (!File.ReadAllText(command).Contains("riverctl", StringComparison.Ordinal))
            {
                return false;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            log.Debug($"failed to read the init file {command}: {e.Message}");
            return false;
        }

        log.Error($"the init file {command} contains the string \"riverctl\". Inlet implements the river " +
            $"window-management protocols, which have no riverctl. An init written for river-classic " +
            $"cannot configure Inlet: it must start a window manager instead");
        return true;
    }
}
