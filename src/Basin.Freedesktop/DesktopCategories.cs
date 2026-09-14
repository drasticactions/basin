namespace Basin.Freedesktop;

public static class DesktopCategories
{
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
