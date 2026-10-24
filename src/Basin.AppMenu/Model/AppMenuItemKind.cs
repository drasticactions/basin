namespace Basin.AppMenu;

/// <summary>How an <see cref="AppMenuItem"/> draws and behaves.</summary>
public enum AppMenuItemKind
{
    /// <summary>An ordinary item or a submenu.</summary>
    Normal,

    /// <summary>A line between groups of items. It has no label and cannot be activated.</summary>
    Separator,

    /// <summary>An item with an on or off state in <see cref="AppMenuItem.ToggleState"/>.</summary>
    Check,

    /// <summary>An item in a group where one item is on, as <see cref="AppMenuItem.ToggleState"/> shows.</summary>
    Radio,
}
