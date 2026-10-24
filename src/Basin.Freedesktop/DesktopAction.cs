namespace Basin.Freedesktop;

/// <summary>
/// One <c>[Desktop Action X]</c> group of a desktop entry that the entry's <c>Actions=</c> key names.
/// <see cref="DesktopEntry.LaunchArgv(DesktopAction, IReadOnlyList{string})"/> starts it.
/// </summary>
public sealed class DesktopAction
{
    private string[]? _argv;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopAction"/> class.
    /// </summary>
    /// <param name="key">The action identifier, the <c>X</c> in the group header.</param>
    /// <param name="name">The localized label of the action.</param>
    /// <param name="icon">The icon name or absolute icon path, or <see langword="null"/> when the group has none.</param>
    /// <param name="exec">The raw <c>Exec</c> value, or <see langword="null"/> when the group has none.</param>
    public DesktopAction(string key, string name, string? icon, string? exec)
    {
        Key = key;
        Name = name;
        Icon = icon;
        Exec = exec;
    }

    /// <summary>
    /// Gets the action identifier, the <c>X</c> in the group header.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the localized label of the action.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the icon name or absolute icon path, or <see langword="null"/> when the group has none.
    /// </summary>
    public string? Icon { get; }

    /// <summary>
    /// Gets the raw <c>Exec</c> value, or <see langword="null"/> when the group has none.
    /// </summary>
    public string? Exec { get; }

    /// <summary>
    /// Gets <see cref="Exec"/> split into arguments by <see cref="ExecLine.Split(string)"/>, with field codes removed. It is computed once and held.
    /// The list is empty when <see cref="Exec"/> is <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<string> Argv => ArgvArray();

    internal string[] ArgvArray() => _argv ??= ExecLine.Split(Exec ?? "");
}
