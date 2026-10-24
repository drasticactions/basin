namespace Basin.AppMenu;

/// <summary>
/// One entry of an <see cref="AppMenu"/>: a title, an item, a separator or a submenu. Labels carry no mnemonic markers.
/// </summary>
public sealed class AppMenuItem
{
    /// <summary>
    /// Gets the id that the source uses for the item. A reader assigns new ids on every rebuild.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets or sets the text of the item, without mnemonic markers.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether the user can activate the item.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a renderer shows the item.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Gets or sets how the item draws and behaves.
    /// </summary>
    public AppMenuItemKind Kind { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a check or radio item is on. Other kinds ignore it.
    /// </summary>
    public bool ToggleState { get; set; }

    /// <summary>
    /// Gets or sets the key chord that the client shows for the item, or <see langword="null"/> when it has none.
    /// </summary>
    public AppMenuShortcut? Shortcut { get; set; }

    /// <summary>
    /// Gets or sets the icon theme name of the item's icon, or <see langword="null"/> when it has none.
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// Gets or sets the item's icon as PNG bytes, or <see langword="null"/> when it has none.
    /// </summary>
    public byte[]? IconData { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item opens a submenu. It can be <see langword="true"/> while <see cref="Children"/> is still empty.
    /// A renderer must not decide that an item is a leaf from an empty <see cref="Children"/> list.
    /// </summary>
    public bool IsSubmenu { get; set; }

    /// <summary>
    /// Gets the items of the submenu, in order. Some clients fill it only after <see cref="IAppMenuSource.AboutToShow"/>.
    /// </summary>
    public List<AppMenuItem> Children { get; } = [];

    internal object? Tag { get; set; }
}
