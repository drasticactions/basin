using Basin.Frames.Metacity;

namespace Basin.Shell.Nested;

/// <summary>
/// The Metacity theme that this assembly embeds. A shell can use it as a theme fallback.
/// </summary>
public static class NestedTheme
{
    private const string ResourceName = "basin-nested-metacity-theme-1.xml";

    /// <summary>
    /// The name that <see cref="Load"/> gives to the embedded theme.
    /// </summary>
    public const string Name = "Nested";

    /// <summary>
    /// Parses the embedded theme as a fallback for <see cref="MetacityFrames.LoadTheme"/>.
    /// It ignores the requested name and always returns the embedded theme named <see cref="Name"/>.
    /// </summary>
    /// <param name="name">The requested theme name. The method does not read it.</param>
    /// <returns>The embedded theme. The value is never <see langword="null"/>.</returns>
    public static MetacityTheme? Load(string name) => Create(Name);

    /// <summary>
    /// Parses the embedded theme and gives it a name.
    /// </summary>
    /// <param name="themeName">The name that the parsed theme reports.</param>
    /// <returns>A new theme parsed from the embedded resource.</returns>
    /// <exception cref="ArgumentException"><paramref name="themeName"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">The embedded resource is missing from the assembly.</exception>
    public static MetacityTheme Create(string themeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(themeName);
        using var stream = typeof(NestedTheme).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the embedded theme '{ResourceName}' is missing");
        return MetacityTheme.Parse(stream, directory: ".", majorVersion: 1, name: themeName);
    }
}
