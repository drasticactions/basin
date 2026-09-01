namespace DeskbarWm;

internal static class LeafMenu
{
    public static IReadOnlyList<MenuItemEntry> Build(Manager manager)
    {
        var config = manager.Configuration;
        var items = new List<MenuItemEntry>
        {
            new("Applications", Children: BuildApplications(manager)),
            new("Recent documents", Children: BuildBookmarks(manager, folders: false)),
            new("Recent folders", Children: BuildBookmarks(manager, folders: true)),
            new("Recent applications", Children: BuildRecentApplications(manager)),
            new(string.Empty, Separator: true),
            new("Deskbar preferences", Children: BuildPreferences(manager)),
            new(string.Empty, Separator: true),
            new("About Deskbar", () => manager.Logger.Info($"deskbar-wm, a BeOS Deskbar on the river seam")),
            new(string.Empty, Separator: true),
            new("Restart", () => SessionActions.Restart(manager.Logger), Enabled: SessionActions.Available),
            new("Shut down", () => SessionActions.ShutDown(manager.Logger), Enabled: SessionActions.Available),
        };
        _ = config;
        return items;
    }

    internal static List<MenuItemEntry> BuildApplications(Manager manager)
    {
        var items = new List<MenuItemEntry>();
        foreach (var app in DesktopEntries.All())
        {
            var entry = app;
            items.Add(new MenuItemEntry(app.Name, () => manager.LaunchApp(entry)));
        }

        if (items.Count == 0)
        {
            items.Add(new MenuItemEntry("No applications found", Enabled: false));
        }

        return items;
    }

    private static List<MenuItemEntry> BuildBookmarks(Manager manager, bool folders)
    {
        var config = manager.Configuration;
        var (documents, folderList) = manager.Recents.ReadBookmarks(
            config.RecentDocumentsCount, config.RecentFoldersCount);
        var source = folders ? folderList : documents;
        var items = new List<MenuItemEntry>();
        foreach (var (path, label) in source)
        {
            var target = path;
            items.Add(new MenuItemEntry(label, () => manager.OpenPath(target)));
        }

        if (items.Count == 0)
        {
            items.Add(new MenuItemEntry(folders ? "No recent folders" : "No recent documents", Enabled: false));
        }

        return items;
    }

    private static List<MenuItemEntry> BuildRecentApplications(Manager manager)
    {
        var items = new List<MenuItemEntry>();
        foreach (var appId in manager.Recents.RecentApplications)
        {
            if (DesktopEntries.EntryFor(appId) is { } app)
            {
                var entry = app;
                items.Add(new MenuItemEntry(app.Name, () => manager.LaunchApp(entry)));
            }
        }

        if (items.Count == 0)
        {
            items.Add(new MenuItemEntry("No recent applications", Enabled: false));
        }

        return items;
    }

    private static List<MenuItemEntry> BuildPreferences(Manager manager)
    {
        var config = manager.Configuration;
        var placement = config.Placement;
        var readsSide = placement.Orientation == BarOrientation.Vertical
            || placement.State == DeskbarState.Mini;
        var readsEnd = placement.Orientation == BarOrientation.Horizontal
            || placement.State == DeskbarState.Mini;
        var items = new List<MenuItemEntry>
        {
            new("Mini state",
                () => manager.SetPlacement(manager.Configuration.Placement with { State = DeskbarState.Mini }),
                Checked: placement.State == DeskbarState.Mini),
            new("Expando state",
                () => manager.SetPlacement(manager.Configuration.Placement with { State = DeskbarState.Expando }),
                Checked: placement.State == DeskbarState.Expando),
            new("Full state",
                () => manager.SetPlacement(manager.Configuration.Placement with
                {
                    State = DeskbarState.Full,
                    Orientation = BarOrientation.Vertical,
                }),
                Checked: placement.State == DeskbarState.Full),
            new(string.Empty, Separator: true),
            new("Vertical",
                () => manager.SetPlacement(manager.Configuration.Placement with
                {
                    Orientation = BarOrientation.Vertical,
                }),
                Checked: placement.Orientation == BarOrientation.Vertical),
            new("Horizontal",
                () => manager.SetPlacement(manager.Configuration.Placement with
                {
                    Orientation = BarOrientation.Horizontal,
                }),
                Checked: placement.Orientation == BarOrientation.Horizontal),
            new(string.Empty, Separator: true),
            new("Left side",
                () => manager.SetPlacement(manager.Configuration.Placement with { Side = BarSide.Left }),
                Enabled: readsSide,
                Checked: placement.Side == BarSide.Left),
            new("Right side",
                () => manager.SetPlacement(manager.Configuration.Placement with { Side = BarSide.Right }),
                Enabled: readsSide,
                Checked: placement.Side == BarSide.Right),
            new("Top",
                () => manager.SetPlacement(manager.Configuration.Placement with { End = BarEnd.Top }),
                Enabled: readsEnd,
                Checked: placement.End == BarEnd.Top),
            new("Bottom",
                () => manager.SetPlacement(manager.Configuration.Placement with { End = BarEnd.Bottom }),
                Enabled: readsEnd,
                Checked: placement.End == BarEnd.Bottom),
            new(string.Empty, Separator: true),
            new("Always on top",
                () => manager.SetDeskbarFlag("always-on-top", config.AlwaysOnTop = !config.AlwaysOnTop),
                Checked: config.AlwaysOnTop),
            new("Auto-raise",
                () => manager.SetDeskbarFlag("auto-raise", config.AutoRaise = !config.AutoRaise),
                Checked: config.AutoRaise),
            new("Auto-hide",
                () => manager.SetDeskbarFlag("auto-hide", config.AutoHide = !config.AutoHide),
                Checked: config.AutoHide),
            new("Show labels",
                () => manager.SetDeskbarFlag("show-labels", config.ShowLabels = !config.ShowLabels),
                Checked: config.ShowLabels),
            new("Sort running applications",
                () => manager.SetDeskbarFlag("sort-teams", config.SortTeams = !config.SortTeams),
                Checked: config.SortTeams),
            new("Expand windows",
                () => manager.SetDeskbarFlag("expand-windows", config.ExpandWindows = !config.ExpandWindows),
                Checked: config.ExpandWindows),
            new(string.Empty, Separator: true),
        };

        foreach (var size in (int[])[16, 24, 32, 48, 64])
        {
            var chosen = size;
            items.Add(new MenuItemEntry(
                $"{size} x {size} icons",
                () => manager.SetIconSize(chosen),
                Checked: config.IconSize == size));
        }

        items.Add(new MenuItemEntry(string.Empty, Separator: true));
        items.Add(new MenuItemEntry(
            "Show seconds",
            () => manager.SetClockFlag("show-seconds", config.ClockShowSeconds = !config.ClockShowSeconds),
            Checked: config.ClockShowSeconds));
        items.Add(new MenuItemEntry(
            "Show day of week",
            () => manager.SetClockFlag("show-day-of-week", config.ClockShowDayOfWeek = !config.ClockShowDayOfWeek),
            Checked: config.ClockShowDayOfWeek));
        items.Add(new MenuItemEntry(
            "Show time zone",
            () => manager.SetClockFlag("show-time-zone", config.ClockShowTimeZone = !config.ClockShowTimeZone),
            Checked: config.ClockShowTimeZone));
        return items;
    }
}
