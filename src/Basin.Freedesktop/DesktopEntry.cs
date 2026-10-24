namespace Basin.Freedesktop;

/// <summary>
/// One parsed desktop entry file. <see cref="DesktopEntryReader"/> creates it from the first <c>[Desktop Entry]</c> group.
/// </summary>
public sealed class DesktopEntry
{
    private string[]? _argv;

    /// <summary>
    /// Gets the desktop file id: the path relative to the scanned directory with <c>/</c> replaced by <c>-</c>, including <c>.desktop</c>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the path of the file that the entry came from. The <c>Path=</c> key is <see cref="WorkingDirectory"/>.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the best localized <c>Name</c> for the reader's locale. It is never empty.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the <c>Type</c> key. A missing or unknown value is <see cref="DesktopEntryType.Unknown"/>.
    /// </summary>
    public DesktopEntryType Type { get; init; }

    /// <summary>
    /// Gets the best localized <c>GenericName</c>, or <see langword="null"/> when the file has none.
    /// </summary>
    public string? GenericName { get; init; }

    /// <summary>
    /// Gets the best localized <c>Comment</c>, or <see langword="null"/> when the file has none.
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Gets the <c>Icon</c> key, an icon name or an absolute path, or <see langword="null"/> when the file has none.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Gets the raw <c>Exec</c> value with field codes still in it, or <see langword="null"/> when the file has none.
    /// </summary>
    public string? Exec { get; init; }

    /// <summary>
    /// Gets the <c>TryExec</c> value that <see cref="IsListable(IReadOnlySet{string}, Func{string, bool})"/> tests, or <see langword="null"/> when the file has none.
    /// </summary>
    public string? TryExec { get; init; }

    /// <summary>
    /// Gets the <c>Path</c> key, the directory to run the program in, or <see langword="null"/> when the file has none.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets a value indicating whether the program must run in a terminal. <see cref="LaunchArgv(IReadOnlyList{string})"/> reads it.
    /// </summary>
    public bool Terminal { get; init; }

    /// <summary>
    /// Gets a value indicating whether the entry is kept out of menus. Lookup still returns such an entry.
    /// </summary>
    public bool NoDisplay { get; init; }

    /// <summary>
    /// Gets a value indicating whether the application can start through D-Bus activation.
    /// </summary>
    public bool DBusActivatable { get; init; }

    /// <summary>
    /// Gets a value indicating whether the application supports startup notification.
    /// </summary>
    public bool StartupNotify { get; init; }

    /// <summary>
    /// Gets the <c>StartupWMClass</c> value that the app id lookup also compares, or <see langword="null"/> when the file has none.
    /// </summary>
    public string? StartupWMClass { get; init; }

    /// <summary>
    /// Gets the <c>URL</c> of a <see cref="DesktopEntryType.Link"/> entry, or <see langword="null"/> when the file has none.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Gets the <c>Categories</c> list in file order. <see cref="DesktopCategories"/> maps it to a main category.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>
    /// Gets the best localized <c>Keywords</c> list. It is empty when the file has none.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>
    /// Gets the MIME types that the application can open. It is empty when the file has none.
    /// </summary>
    public IReadOnlyList<string> MimeType { get; init; } = [];

    /// <summary>
    /// Gets the desktops that the entry is listed in exclusively. An empty list means all desktops.
    /// </summary>
    public IReadOnlyList<string> OnlyShowIn { get; init; } = [];

    /// <summary>
    /// Gets the desktops that the entry is not listed in.
    /// </summary>
    public IReadOnlyList<string> NotShowIn { get; init; } = [];

    /// <summary>
    /// Gets the interfaces that the application declares in <c>Implements</c>.
    /// </summary>
    public IReadOnlyList<string> Implements { get; init; } = [];

    /// <summary>
    /// Gets the <c>DesktopNames</c> list of a session entry. It is empty on an application entry.
    /// </summary>
    public IReadOnlyList<string> DesktopNames { get; init; } = [];

    /// <summary>
    /// Gets the actions that <c>Actions=</c> names, in that order. A group that the key does not name is not included.
    /// </summary>
    public IReadOnlyList<DesktopAction> Actions { get; init; } = [];

    /// <summary>
    /// Gets <see cref="Exec"/> split into arguments by <see cref="ExecLine.Split(string)"/>. It is computed once and held.
    /// The list is empty when <see cref="Exec"/> is <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<string> Argv => _argv ??= ExecLine.Split(Exec ?? "");

    /// <summary>
    /// Gets <see cref="Argv"/> joined again by <see cref="ExecLine.Join(ReadOnlySpan{string})"/>, without field codes. It is a valid <c>sh -c</c> line.
    /// </summary>
    public string CommandLine => ExecLine.Join(ArgvArray());

    /// <summary>
    /// Applies the menu rule, with <c>TryExec</c> tested against this machine's <c>PATH</c>.
    /// </summary>
    /// <param name="currentDesktop">The desktop names of the session, or <see langword="null"/> when the consumer has none.</param>
    /// <returns><see langword="true"/> when a menu can show the entry.</returns>
    public bool IsListable(IReadOnlySet<string>? currentDesktop = null) => IsListable(currentDesktop, null);

    /// <summary>
    /// Applies the menu rule. The entry must be an application, not <see cref="NoDisplay"/>, with an <see cref="Exec"/> and a <see cref="TryExec"/> that resolves.
    /// <see cref="OnlyShowIn"/> and <see cref="NotShowIn"/> are then checked against <paramref name="currentDesktop"/>. <see cref="Terminal"/> is not part of the rule.
    /// </summary>
    /// <param name="currentDesktop">The desktop names of the session. With <see langword="null"/>, an entry with <see cref="OnlyShowIn"/> is not listable.</param>
    /// <param name="tryExec">A test for the <c>TryExec</c> program, or <see langword="null"/> to look for an absolute path or a file on <c>PATH</c>.</param>
    /// <returns><see langword="true"/> when a menu can show the entry.</returns>
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

    /// <summary>
    /// Makes the argument vector that starts the entry.
    /// </summary>
    /// <param name="terminal">The terminal command that takes the program as its remaining arguments, for example <c>xterm -e</c>. It is used only for a <see cref="Terminal"/> entry.</param>
    /// <returns>A new array that the caller owns. It is <see langword="null"/> when <see cref="Argv"/> is empty, or for a <see cref="Terminal"/> entry when <paramref name="terminal"/> is null or empty.</returns>
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
