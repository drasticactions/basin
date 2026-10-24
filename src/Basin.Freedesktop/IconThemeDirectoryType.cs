namespace Basin.Freedesktop;

/// <summary>
/// The <c>Type=</c> key of an icon theme directory, which sets the sizes that the directory matches.
/// </summary>
public enum IconThemeDirectoryType
{
    /// <summary>The directory matches sizes within <see cref="IconThemeDirectory.Threshold"/> of its size. This is the default.</summary>
    Threshold,
    /// <summary>The directory matches only its own size.</summary>
    Fixed,
    /// <summary>The <c>Scalable</c> type. The directory matches any size from <see cref="IconThemeDirectory.MinSize"/> to <see cref="IconThemeDirectory.MaxSize"/>.</summary>
    Scaled,
}
