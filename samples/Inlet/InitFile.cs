using Basin.Diagnostics;
using Basin.Host;

namespace Inlet;

internal static class InitFile
{
    public static bool TryResolve(string? command, BasinLogger log, out string? startup)
    {
        startup = null;

        if (command is not null)
        {
            startup = command;
        }
        else if (!InitProcess.TryFind(["inlet", "river"], log, out startup))
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
