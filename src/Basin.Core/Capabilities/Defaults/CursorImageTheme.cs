namespace Basin.Capabilities.Defaults;

/// <summary>
/// Resolves cursor shapes from a <see cref="CursorImages"/> set, for example a loaded XCursor theme.
/// </summary>
public sealed class CursorImageTheme : ICursorTheme
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CursorImageTheme"/> class.
    /// </summary>
    /// <param name="images">The cursor images to resolve from, or <see langword="null"/> to resolve nothing until <see cref="Images"/> is set.</param>
    public CursorImageTheme(CursorImages? images = null) => Images = images;

    /// <summary>
    /// Gets or sets the cursor images to resolve from. With <see langword="null"/>, every lookup fails.
    /// </summary>
    public CursorImages? Images { get; set; }

    /// <inheritdoc/>
    public bool TryResolve(string shapeName, double scale, out CursorImage image)
    {
        if (Images?.Named(shapeName, new CursorKey(scale, null)) is { } found)
        {
            image = found;
            return true;
        }

        image = default;
        return false;
    }
}
