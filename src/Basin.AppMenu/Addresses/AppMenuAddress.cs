namespace Basin.AppMenu;

/// <summary>
/// Where a client exports the menus of a surface on the session bus. A dbusmenu address uses <see cref="ObjectPath"/>, and a GMenu address uses the other four paths.
/// Unused paths are empty strings.
/// </summary>
/// <param name="Kind">The menu protocol that the client exports.</param>
/// <param name="BusName">The bus name of the client, usually its unique name.</param>
/// <param name="ObjectPath">The com.canonical.dbusmenu object path of a dbusmenu address.</param>
/// <param name="MenubarPath">The org.gtk.Menus path of the menu bar of a GMenu address.</param>
/// <param name="AppMenuPath">The org.gtk.Menus path of the application menu of a GMenu address.</param>
/// <param name="ApplicationPath">The org.gtk.Actions path of the <c>app.</c> actions of a GMenu address.</param>
/// <param name="WindowPath">The org.gtk.Actions path of the <c>win.</c> actions of a GMenu address.</param>
public sealed record AppMenuAddress(
    AppMenuAddressKind Kind,
    string BusName,
    string ObjectPath = "",
    string MenubarPath = "",
    string AppMenuPath = "",
    string ApplicationPath = "",
    string WindowPath = "")
{
    /// <summary>
    /// Makes a dbusmenu address.
    /// </summary>
    /// <param name="busName">The bus name of the client.</param>
    /// <param name="objectPath">The com.canonical.dbusmenu object path.</param>
    /// <returns>An address of kind <see cref="AppMenuAddressKind.DbusMenu"/>.</returns>
    public static AppMenuAddress DbusMenu(string busName, string objectPath) => new(AppMenuAddressKind.DbusMenu, busName, objectPath);
}
