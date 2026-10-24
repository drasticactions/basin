namespace Basin.Freedesktop;

/// <summary>
/// One bucket that <see cref="DesktopCategories.Group(IEnumerable{DesktopEntry})"/> makes.
/// </summary>
/// <param name="Category">The main category that all entries in the bucket share.</param>
/// <param name="Entries">The entries of the bucket, never empty, in the order that the caller gave them.</param>
public sealed record DesktopCategoryGroup(DesktopMainCategory Category, IReadOnlyList<DesktopEntry> Entries);
