namespace Basin.Freedesktop;

public sealed class DesktopEntry
{
    private string[]? _argv;

    public required string Id { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public DesktopEntryType Type { get; init; }

    public string? GenericName { get; init; }

    public string? Comment { get; init; }

    public string? Icon { get; init; }

    public string? Exec { get; init; }

    public string? TryExec { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool Terminal { get; init; }

    public bool NoDisplay { get; init; }

    public bool DBusActivatable { get; init; }

    public bool StartupNotify { get; init; }

    public string? StartupWMClass { get; init; }

    public string? Url { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = [];

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public IReadOnlyList<string> MimeType { get; init; } = [];

    public IReadOnlyList<string> OnlyShowIn { get; init; } = [];

    public IReadOnlyList<string> NotShowIn { get; init; } = [];

    public IReadOnlyList<string> Implements { get; init; } = [];

    public IReadOnlyList<DesktopAction> Actions { get; init; } = [];

    public IReadOnlyList<string> Argv => _argv ??= ExecLine.Split(Exec ?? "");

    public string CommandLine => ExecLine.Join(ArgvArray());

    public bool IsListable(IReadOnlySet<string>? currentDesktop = null) => IsListable(currentDesktop, null);

    /// <summary>
    /// The menu rule, with <paramref name="tryExec"/> answering whether a <c>TryExec</c> value
    /// resolves. Null asks this machine's <c>PATH</c>; a consumer whose entries came from another
    /// machine passes what that machine said.
    /// </summary>
    public bool IsListable(IReadOnlySet<string>? currentDesktop, Func<string, bool>? tryExec)
    {
        if (Type != DesktopEntryType.Application || NoDisplay || Exec is null)
        {
            return false;
        }

        if (TryExec is { Length: > 0 } candidate && !(tryExec ?? Resolves)(candidate))
        {
            return false;
        }

        if (OnlyShowIn.Count > 0 && (currentDesktop is null || !Intersects(OnlyShowIn, currentDesktop)))
        {
            return false;
        }

        return currentDesktop is null || !Intersects(NotShowIn, currentDesktop);
    }

    public string[]? LaunchArgv(IReadOnlyList<string>? terminal)
    {
        var argv = ArgvArray();
        if (argv.Length == 0)
        {
            return null;
        }

        if (!Terminal)
        {
            return (string[])argv.Clone();
        }

        if (terminal is not { Count: > 0 })
        {
            return null;
        }

        var launch = new string[terminal.Count + argv.Length];
        for (var i = 0; i < terminal.Count; i++)
        {
            launch[i] = terminal[i];
        }

        argv.CopyTo(launch, terminal.Count);
        return launch;
    }

    private string[] ArgvArray() => _argv ??= ExecLine.Split(Exec ?? "");

    private static bool Intersects(IReadOnlyList<string> names, IReadOnlySet<string> desktop)
    {
        foreach (var name in names)
        {
            if (desktop.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Resolves(string executable)
    {
        if (System.IO.Path.IsPathRooted(executable))
        {
            return File.Exists(executable);
        }

        if (executable.Contains('/'))
        {
            return false;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            if (directory.Length > 0 && File.Exists(System.IO.Path.Combine(directory, executable)))
            {
                return true;
            }
        }

        return false;
    }
}
