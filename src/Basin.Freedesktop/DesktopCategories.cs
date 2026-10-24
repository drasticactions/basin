namespace Basin.Freedesktop;

/// <summary>
/// Maps the flat <see cref="DesktopEntry.Categories"/> list onto the registered main categories, without a <c>.menu</c> file.
/// </summary>
public static class DesktopCategories
{
    /// <summary>
    /// Maps one category string onto its registered main category. <c>Audio</c> and <c>Video</c> map to <see cref="DesktopMainCategory.AudioVideo"/>.
    /// </summary>
    /// <param name="category">A value from <c>Categories=</c>, compared with case.</param>
    /// <returns>The main category, or <see langword="null"/> when the string is not a registered main category.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> is <see langword="null"/>.</exception>
    public static DesktopMainCategory? Registered(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return category switch
        {
            "AudioVideo" or "Audio" or "Video" => DesktopMainCategory.AudioVideo,
            "Development" => DesktopMainCategory.Development,
            "Education" => DesktopMainCategory.Education,
            "Game" => DesktopMainCategory.Game,
            "Graphics" => DesktopMainCategory.Graphics,
            "Network" => DesktopMainCategory.Network,
            "Office" => DesktopMainCategory.Office,
            "Science" => DesktopMainCategory.Science,
            "Settings" => DesktopMainCategory.Settings,
            "System" => DesktopMainCategory.System,
            "Utility" => DesktopMainCategory.Utility,
            _ => null,
        };
    }

    /// <summary>
    /// Gets the English menu name that the default layout of the Desktop Menu specification gives a main category.
    /// </summary>
    /// <param name="category">The main category.</param>
    /// <returns>The label, for example "Programming" or "Accessories". An unknown value gives "Other".</returns>
    public static string DefaultLabel(DesktopMainCategory category) => category switch
    {
        DesktopMainCategory.AudioVideo => "Sound & Video",
        DesktopMainCategory.Development => "Programming",
        DesktopMainCategory.Education => "Education",
        DesktopMainCategory.Game => "Games",
        DesktopMainCategory.Graphics => "Graphics",
        DesktopMainCategory.Network => "Internet",
        DesktopMainCategory.Office => "Office",
        DesktopMainCategory.Science => "Science",
        DesktopMainCategory.Settings => "Settings",
        DesktopMainCategory.System => "System Tools",
        DesktopMainCategory.Utility => "Accessories",
        _ => "Other",
    };

    /// <summary>
    /// Finds the first registered main category that an entry names.
    /// </summary>
    /// <param name="entry">The entry whose <see cref="DesktopEntry.Categories"/> are read in order.</param>
    /// <returns>The first match, or <see cref="DesktopMainCategory.Other"/> when the entry names none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    public static DesktopMainCategory MainOf(DesktopEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        foreach (var category in entry.Categories)
        {
            if (Registered(category) is { } main)
            {
                return main;
            }
        }

        return DesktopMainCategory.Other;
    }

    /// <summary>
    /// Puts each entry into the group of its <see cref="MainOf(DesktopEntry)"/> category.
    /// </summary>
    /// <param name="entries">The entries to group. Their order is kept within each group.</param>
    /// <returns>One group per category that has entries, in the enum order with <see cref="DesktopMainCategory.Other"/> last. Empty groups are omitted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<DesktopCategoryGroup> Group(IEnumerable<DesktopEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var buckets = new List<DesktopEntry>?[(int)DesktopMainCategory.Other + 1];
        foreach (var entry in entries)
        {
            var main = (int)MainOf(entry);
            (buckets[main] ??= []).Add(entry);
        }

        var groups = new List<DesktopCategoryGroup>();
        for (var i = 0; i < buckets.Length; i++)
        {
            if (buckets[i] is { } bucket)
            {
                groups.Add(new DesktopCategoryGroup((DesktopMainCategory)i, bucket));
            }
        }

        return groups;
    }
}
