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
        else
        {
            var search = InitProcess.Find(["inlet", "river"], log);
            if (search.NotExecutable)
            {
                return false;
            }

            startup = search.Path;
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
            log.Debug(e, $"init: {command} not read");
            return false;
        }

        log.Error($"init: {command} uses riverctl, which Inlet does not have, start a window manager from the init instead");
        return true;
    }
}
