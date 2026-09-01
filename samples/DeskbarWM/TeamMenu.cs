namespace DeskbarWm;

internal static class TeamMenu
{
    public static IReadOnlyList<MenuItemEntry> Build(Manager manager, Team team)
    {
        var items = new List<MenuItemEntry>();
        foreach (var mw in team.Windows)
        {
            var window = mw;
            items.Add(new MenuItemEntry(
                window.Window.Title ?? window.Window.AppId ?? "?",
                () => manager.ActivateWindow(window)));
        }

        items.Add(new MenuItemEntry(string.Empty, Separator: true));
        items.Add(new MenuItemEntry("Hide all", () => manager.HideTeam(team)));
        items.Add(new MenuItemEntry("Close all", () => manager.CloseTeam(team)));
        return items;
    }
}
