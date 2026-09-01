namespace DeskbarWm;

internal static class DesktopMenu
{
    public static IReadOnlyList<MenuItemEntry> Build(Manager manager)
    {
        var items = new List<MenuItemEntry>
        {
            new("Applications", Children: LeafMenu.BuildApplications(manager)),
            new(string.Empty, Separator: true),
        };

        var grid = manager.Workspaces;
        for (var i = 0; i < grid.Count; i++)
        {
            var index = i;
            items.Add(new MenuItemEntry(
                $"Workspace {i + 1}",
                () => manager.SwitchWorkspaceFromMenu(index),
                Checked: grid.Current == i));
        }

        return items;
    }
}
