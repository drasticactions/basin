namespace Basin.AppMenu;

/// <summary>
/// A source of one window's menus, imported from a client or made by the compositor. Its members must be used on the compositor thread.
/// </summary>
public interface IAppMenuSource : IDisposable
{
    /// <summary>
    /// Gets the menu protocol that the source reads.
    /// </summary>
    AppMenuAddressKind Kind { get; }

    /// <summary>
    /// Gets the menu that the source fills. The instance does not change, and its <see cref="AppMenu.Changed"/> event reports new content.
    /// </summary>
    AppMenu Menu { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Menu"/> holds a complete menu from the source.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Occurs once when the source cannot read its first menu. A failure after the menu is ready keeps the last menu and raises nothing.
    /// </summary>
    event Action? Failed;

    /// <summary>
    /// Tells the source that a submenu will open, so that the client can fill or update it.
    /// </summary>
    /// <param name="item">The submenu item that opens.</param>
    void AboutToShow(AppMenuItem item);

    /// <summary>
    /// Runs the action of an item.
    /// </summary>
    /// <param name="item">The item that the user chose.</param>
    /// <param name="timestamp">The input event time in milliseconds.</param>
    void Activate(AppMenuItem item, uint timestamp);
}
