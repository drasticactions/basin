namespace Basin.Freedesktop;

/// <summary>
/// One <c>[Desktop Action X]</c> group of a desktop entry that the entry's <c>Actions=</c> key names.
/// </summary>
/// <param name="Key">The action identifier, the <c>X</c> in the group header.</param>
/// <param name="Name">The localized label of the action.</param>
/// <param name="Icon">The icon name or absolute icon path, or <see langword="null"/> when the group has none.</param>
/// <param name="Exec">The raw <c>Exec</c> value, or <see langword="null"/> when the group has none.</param>
public sealed record DesktopAction(string Key, string Name, string? Icon, string? Exec)
{
    /// <summary>
    /// Gets the argument vector of <see cref="Exec"/>, split by <see cref="ExecLine.Split(string)"/> with field codes removed.
    /// Each read splits again and returns a new array. It is empty when <see cref="Exec"/> is <see langword="null"/>.
    /// </summary>
    public string[] Argv => ExecLine.Split(Exec ?? "");
}
