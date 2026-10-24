namespace Basin.Config;

/// <summary>
/// One <c>[[launch]]</c> rule that matches a program before it starts and chooses its X server.
/// </summary>
public sealed record LaunchRule
{
    /// <summary>
    /// Gets the desktop entry ID to match, or <see langword="null"/> to match any. The <c>.desktop</c> suffix is optional.
    /// </summary>
    public string? DesktopId { get; init; }

    /// <summary>
    /// Gets the base name of the program to match, or <see langword="null"/> to match any.
    /// </summary>
    public string? Exec { get; init; }

    /// <summary>
    /// Gets the X server density that the rule chooses, or <see langword="null"/> when the rule chooses none.
    /// </summary>
    public X11Density? X11 { get; init; }

    /// <summary>
    /// Gets the rank of the rule: 2 for <see cref="DesktopId"/> plus 1 for <see cref="Exec"/>. A higher rank wins.
    /// </summary>
    public int Specificity => (DesktopId is not null ? 2 : 0) + (Exec is not null ? 1 : 0);

    /// <summary>
    /// Tells whether the rule matches a launch. Every key that the rule sets must match.
    /// </summary>
    /// <param name="desktopId">The desktop entry ID of the launch, or <see langword="null"/>.</param>
    /// <param name="exec">The program path or name. Only its base name is compared.</param>
    /// <returns><see langword="false"/> when the rule sets no key, or when a key that it sets does not match.</returns>
    public bool Matches(string? desktopId, string? exec)
    {
        if (DesktopId is null && Exec is null)
        {
            return false;
        }

        if (DesktopId is not null && (desktopId is null || !SameDesktopId(DesktopId, desktopId)))
        {
            return false;
        }

        return Exec is null || (exec is not null && string.Equals(Exec, Path.GetFileName(exec), StringComparison.Ordinal));
    }

    /// <summary>
    /// Adds the <c>.desktop</c> suffix to a desktop entry ID that does not have it.
    /// </summary>
    /// <param name="desktopId">The desktop entry ID.</param>
    /// <returns>The ID with the <c>.desktop</c> suffix.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="desktopId"/> is <see langword="null"/>.</exception>
    public static string NormalizeDesktopId(string desktopId)
    {
        ArgumentNullException.ThrowIfNull(desktopId);
        return desktopId.EndsWith(".desktop", StringComparison.Ordinal) ? desktopId : desktopId + ".desktop";
    }

    private static bool SameDesktopId(string a, string b) =>
        string.Equals(NormalizeDesktopId(a), NormalizeDesktopId(b), StringComparison.Ordinal);
}
