namespace Basin.Freedesktop;

public sealed record DesktopCategoryGroup(DesktopMainCategory Category, IReadOnlyList<DesktopEntry> Entries);
