namespace Basin.Themes.MsStyles;

/// <summary>
/// Holds the descriptive text of a theme from its <c>documentation</c> class. A property is <see langword="null"/> when the theme does not set it or sets it to empty text.
/// </summary>
public sealed record MsStylesDocumentation
{
    /// <summary>
    /// Gets the internal name of the theme.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the name of the theme that a user sees.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the tooltip text of the theme.
    /// </summary>
    public string? Tooltip { get; init; }

    /// <summary>
    /// Gets the author of the theme.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets the company that made the theme.
    /// </summary>
    public string? Company { get; init; }

    /// <summary>
    /// Gets the copyright text of the theme.
    /// </summary>
    public string? Copyright { get; init; }

    /// <summary>
    /// Gets the web address of the theme author.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Gets the version text of the theme.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the description of the theme.
    /// </summary>
    public string? Description { get; init; }
}
