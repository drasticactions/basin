using Basin.Frames.Metacity;

namespace Basin.Shell.Nested;

/// <summary>
/// The Metacity theme that this assembly embeds. A shell can use it as a theme fallback.
/// </summary>
public static class NestedTheme
{
    private const string ResourceName = "basin-nested-metacity-theme-1.xml";

    /// <summary>
    /// The name that <see cref="Always"/> gives to the embedded theme.
    /// </summary>
    public const string Name = "Nested";

    /// <summary>
    /// Parses the embedded theme as the catch-all fallback for <see cref="MetacityFrames.LoadTheme"/>. It answers every name, so it must be the last fallback.
    /// It returns the embedded theme named <see cref="Name"/>, and logs at info the name it stands in for.
    /// </summary>
    /// <param name="name">The requested theme name, which the log names.</param>
    /// <returns>The embedded theme. The value is never <see langword="null"/>.</returns>
    public static MetacityTheme? Always(string name)
    {
        if (name != Name)
        {
            NestedLog.Log.Info($"theme: '{name}' not found, the built-in '{Name}' theme is used");
        }

        return Create(Name);
    }

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
            ?? throw new InvalidOperationException($"The embedded theme '{ResourceName}' is missing from the assembly.");
        return MetacityTheme.Parse(stream, directory: ".", majorVersion: 1, name: themeName);
    }
}
