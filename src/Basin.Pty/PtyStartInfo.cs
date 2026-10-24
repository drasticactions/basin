namespace Basin.Pty;

/// <summary>
/// Describes the program that <see cref="PtySession.TryStart"/> runs on a new pseudo-terminal.
/// </summary>
/// <param name="Argv">The program name and its arguments. The first item is looked up on <c>PATH</c> and must not be empty.</param>
public sealed record PtyStartInfo(IReadOnlyList<string> Argv)
{
    /// <summary>
    /// Gets the directory the child starts in. <see langword="null"/> keeps the compositor's current directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets the changes to the compositor's environment that the child receives. A <see langword="null"/> value removes the variable.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();

    /// <summary>
    /// Gets the initial terminal width in character cells. The default is 80.
    /// </summary>
    public int Columns { get; init; } = 80;

    /// <summary>
    /// Gets the initial terminal height in character cells. The default is 24.
    /// </summary>
    public int Rows { get; init; } = 24;

    /// <summary>
    /// Gets the initial terminal width in pixels that the child reads from <c>TIOCGWINSZ</c>. The default is 0, which means unknown.
    /// </summary>
    public int PixelWidth { get; init; }

    /// <summary>
    /// Gets the initial terminal height in pixels that the child reads from <c>TIOCGWINSZ</c>. The default is 0, which means unknown.
    /// </summary>
    public int PixelHeight { get; init; }
}
