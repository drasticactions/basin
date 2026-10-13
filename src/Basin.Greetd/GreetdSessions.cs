using Basin.Freedesktop;
using static Basin.Greetd.GreetdLog;

namespace Basin.Greetd;

public static class GreetdSessions
{
    public static IReadOnlyList<GreetdSession> List(GreetdSessionsOptions? options = null)
    {
        options ??= new GreetdSessionsOptions();
        var locale = options.Locale ?? DesktopLocale.FromEnvironment();
        var sessions = new List<GreetdSession>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        Add(sessions, taken, new DesktopEntries(locale, options.DataDirectories, "wayland-sessions"), GreetdSessionType.Wayland, [], options);
        if (options.X11Wrapper.Count > 0)
        {
            Add(sessions, taken, new DesktopEntries(locale, options.DataDirectories, "xsessions"), GreetdSessionType.X11, options.X11Wrapper, options);
        }

        if (options.EnvironmentsPath is { Length: > 0 } environments)
        {
            AddEnvironments(sessions, taken, environments);
        }

        return sessions;
    }

    public static GreetdSession FreeCommand(string commandLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        return new GreetdSession("command", commandLine, GreetdSessionType.Command, [commandLine], [], [], null);
    }

    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return ExecLine.Join([argument]);
    }

    private static void Add(List<GreetdSession> sessions, HashSet<string> taken, DesktopEntries entries, GreetdSessionType type, IReadOnlyList<string> wrapper, GreetdSessionsOptions options)
    {
        foreach (var entry in entries.All())
        {
            if (!IsSessionListable(entry, options.TryExec))
            {
                continue;
            }

            var argv = entry.Argv;
            if (argv.Count == 0)
            {
                Log.Debug($"{entry.Path}: no Exec, not offered as a session");
                continue;
            }

            var id = entry.Id[..^".desktop".Length];
            if (!taken.Add(id))
            {
                Log.Debug($"{entry.Path}: session id {id} is already taken");
                continue;
            }

            var env = new List<string> { type == GreetdSessionType.X11 ? "XDG_SESSION_TYPE=x11" : "XDG_SESSION_TYPE=wayland" };
            if (entry.DesktopNames.Count > 0)
            {
                env.Add("XDG_CURRENT_DESKTOP=" + string.Join(':', entry.DesktopNames));
                env.Add("XDG_SESSION_DESKTOP=" + entry.DesktopNames[0]);
            }

            sessions.Add(new GreetdSession(id, entry.Name, type, [.. argv], env, wrapper, entry));
        }
    }

    private static bool IsSessionListable(DesktopEntry entry, Func<string, bool>? tryExec)
    {
        var desktops = entry.OnlyShowIn.Count > 0 ? new HashSet<string>(entry.OnlyShowIn, StringComparer.Ordinal) : null;
        return entry.IsListable(desktops, tryExec);
    }

    private static void AddEnvironments(List<GreetdSession> sessions, HashSet<string> taken, string path)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (error is not FileNotFoundException and not DirectoryNotFoundException)
            {
                Log.Info($"{path}: not read ({error.Message})");
            }

            return;
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (!taken.Add(line))
            {
                continue;
            }

            sessions.Add(new GreetdSession(line, line, GreetdSessionType.Command, [line], [], [], null));
        }
    }
}
