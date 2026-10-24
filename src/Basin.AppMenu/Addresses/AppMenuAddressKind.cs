namespace Basin.AppMenu;

/// <summary>The menu protocol that a client exports on the session bus.</summary>
public enum AppMenuAddressKind
{
    /// <summary>com.canonical.dbusmenu, as Qt and other toolkits export it.</summary>
    DbusMenu,

    /// <summary>org.gtk.Menus with org.gtk.Actions, as GTK exports it.</summary>
    GMenu,
}
