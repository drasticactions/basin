namespace DeskbarWm;

internal sealed record MenuItemEntry(
    string Label,
    Action? Activate = null,
    IReadOnlyList<MenuItemEntry>? Children = null,
    bool Separator = false,
    bool Enabled = true,
    bool Checked = false);
