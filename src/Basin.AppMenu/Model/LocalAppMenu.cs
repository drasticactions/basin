namespace Basin.AppMenu;

/// <summary>
/// An <see cref="IAppMenuSource"/> for menus that the compositor makes itself. It runs an action for each item and is always ready.
/// </summary>
/// <param name="commandModifiers">The modifiers that a command letter given to <see cref="Item"/> gets in its shortcut.</param>
public sealed class LocalAppMenu(AppMenuModifiers commandModifiers = AppMenuModifiers.Control) : IAppMenuSource
{
    private readonly Dictionary<int, Action> _actions = [];
    private readonly Dictionary<int, Action> _opening = [];
    private int _next = 1;

    /// <summary>
    /// Gets <see cref="AppMenuAddressKind.DbusMenu"/>. A local menu has no address, and this value only fills the contract.
    /// </summary>
    public AppMenuAddressKind Kind => AppMenuAddressKind.DbusMenu;

    /// <inheritdoc/>
    public AppMenu Menu { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the menu is ready. It is always <see langword="true"/>.
    /// </summary>
    public bool IsReady => true;

    /// <summary>
    /// Never occurs. A local menu cannot fail, so handlers are not kept.
    /// </summary>
    public event Action? Failed
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Replaces the titles of <see cref="Menu"/> and raises its <see cref="AppMenu.Changed"/> event.
    /// </summary>
    /// <param name="titles">The new titles, made with <see cref="Submenu"/> or <see cref="Item"/>.</param>
    public void Set(params AppMenuItem[] titles)
    {
        Menu.Replace(titles);
    }

    /// <summary>
    /// Forgets every action and starts item ids at 1 again. It does not change <see cref="Menu"/>, so call <see cref="Set"/> next.
    /// </summary>
    public void Clear()
    {
        _actions.Clear();
        _opening.Clear();
        _next = 1;
    }

    /// <summary>
    /// Makes an item with a new id. The item is enabled only when <paramref name="enabled"/> is <see langword="true"/> and <paramref name="action"/> is not <see langword="null"/>.
    /// </summary>
    /// <param name="label">The text of the item.</param>
    /// <param name="action">The action that <see cref="Activate"/> runs, or <see langword="null"/> for a disabled item.</param>
    /// <param name="command">A letter that becomes the shortcut key, lowercase, with the constructor's modifiers. <see langword="null"/> gives no shortcut.</param>
    /// <param name="enabled"><see langword="false"/> to disable the item even with an action.</param>
    /// <param name="check">The state of a check item, or <see langword="null"/> for a normal item.</param>
    /// <param name="iconName">The icon theme name of the icon, or <see langword="null"/>.</param>
    /// <returns>The new item.</returns>
    public AppMenuItem Item(string label, Action? action, char? command = null, bool enabled = true, bool? check = null, string? iconName = null)
    {
        var item = new AppMenuItem
        {
            Id = _next++,
            Label = label,
            Enabled = enabled && action is not null,
            Kind = check is null ? AppMenuItemKind.Normal : AppMenuItemKind.Check,
            ToggleState = check ?? false,
            Shortcut = command is { } letter ? new AppMenuShortcut(commandModifiers, char.ToLowerInvariant(letter).ToString()) : null,
            IconName = iconName,
        };
        if (action is not null)
        {
            _actions[item.Id] = action;
        }

        return item;
    }

    /// <summary>
    /// Makes a separator with a new id.
    /// </summary>
    /// <returns>The new separator item.</returns>
    public AppMenuItem Separator() => new() { Id = _next++, Kind = AppMenuItemKind.Separator };

    /// <summary>
    /// Makes a submenu or a title with a new id.
    /// </summary>
    /// <param name="label">The text of the submenu.</param>
    /// <param name="children">The items of the submenu, in order.</param>
    /// <param name="opening">The action that <see cref="AboutToShow"/> runs, or <see langword="null"/>.</param>
    /// <param name="enabled"><see langword="false"/> to disable the submenu.</param>
    /// <param name="iconName">The icon theme name of the icon, or <see langword="null"/>.</param>
    /// <returns>The new submenu item.</returns>
    public AppMenuItem Submenu(string label, IEnumerable<AppMenuItem> children, Action? opening = null, bool enabled = true, string? iconName = null)
    {
        var item = new AppMenuItem { Id = _next++, Label = label, IsSubmenu = true, Enabled = enabled, IconName = iconName };
        item.Children.AddRange(children);
        if (opening is not null)
        {
            _opening[item.Id] = opening;
        }

        return item;
    }

    /// <summary>
    /// Runs the opening action given to <see cref="Submenu"/> for the item, if it has one.
    /// </summary>
    /// <param name="item">The submenu item that opens.</param>
    public void AboutToShow(AppMenuItem item)
    {
        if (_opening.TryGetValue(item.Id, out var opening))
        {
            opening();
        }
    }

    /// <summary>
    /// Runs the action given to <see cref="Item"/> for the item, if it has one. The timestamp is not used.
    /// </summary>
    /// <param name="item">The item that the user chose.</param>
    /// <param name="timestamp">The input event time in milliseconds.</param>
    public void Activate(AppMenuItem item, uint timestamp)
    {
        if (_actions.TryGetValue(item.Id, out var action))
        {
            action();
        }
    }

    /// <summary>
    /// Does nothing. A local menu holds no resources.
    /// </summary>
    public void Dispose()
    {
    }
}
