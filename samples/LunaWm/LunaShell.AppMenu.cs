using Basin;
using Basin.AppMenu;
using Basin.Freedesktop;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private readonly AppMenuAddresses _menuAddresses = new() { ShowsMenuBar = true };
    private readonly Dictionary<string, AppMenuItem> _menuItems = [];
    private readonly HashSet<int> _expanded = [];
    private AppMenuBus? _menuBus;
    private AppMenuRegistrar? _registrar;
    private IAppMenuSource? _menuSource;
    private DesktopEntries? _desktop;

    private AppMenuBus MenuBus => _menuBus ??= new AppMenuBus(_host.Loop);

    private DesktopEntries Desktop => _desktop ??= new DesktopEntries();

    private void InstallAppMenu()
    {
        _services.Use(_menuAddresses);
        _services.Install(AppMenuPack.Default);
    }

    private void AttachAppMenu()
    {
        if (MenuBus.HasAddress)
        {
            _registrar = new AppMenuRegistrar(MenuBus);
            _registrar.Changed += windowId =>
            {
                foreach (var card in _cards)
                {
                    if (card.X11 is { } x11 && x11.WindowId == windowId && card.Mapped)
                    {
                        ResolveMenu(card);
                    }
                }
            };
        }

        _menuAddresses.Changed += (surface, address) =>
        {
            if (OwnerOf(surface) is { Mapped: true } card && ReferenceEquals(card.Surface, surface))
            {
                ImportMenu(card, address);
            }
        };
    }

    private void ResolveMenu(Card card) => ImportMenu(card, AppMenuImport.Resolve(
        _menuAddresses,
        card.Surface,
        card.X11?.AppMenuServiceName,
        card.X11?.AppMenuObjectPath,
        _registrar,
        card.X11?.WindowId));

    private void ImportMenu(Card card, AppMenuAddress? address)
    {
        if (card.MenuImport is null)
        {
            if (address is null)
            {
                return;
            }

            var import = new AppMenuImport(MenuBus, _services.Find<Basin.Shell.Xdg.XdgToplevelSource>(), card.Xdg);
            import.Ready += reader =>
            {
                _report.Line($"APPMENU {card.Id} {(reader.Kind == AppMenuAddressKind.GMenu ? "gmenu" : "dbusmenu")} titles={reader.Menu.Items.Count}");
                RefreshAppMenu(card);
            };
            import.Failed += _ =>
            {
                _report.Line($"APPMENU {card.Id} failed, local");
                RefreshAppMenu(card);
            };
            import.Cleared += () => RefreshAppMenu(card);
            card.MenuImport = import;
        }

        card.MenuImport.Import(address);
    }

    private void RefreshAppMenu(Card card)
    {
        foreach (var view in _views)
        {
            if (view.Menu is { Open: true, Kind: "app" } menu && ReferenceEquals(view.Deck.ActiveCard, card.Parent ?? card))
            {
                FillAppMenu(view, menu);
            }
        }
    }

    private IAppMenuSource MenuSourceFor(Card card)
    {
        if (card.MenuImport?.Source is { IsReady: true } imported)
        {
            return imported;
        }

        var local = card.LocalMenu ??= new LocalAppMenu();
        local.Clear();
        var items = new List<AppMenuItem>
        {
            local.Item("Close", () => CloseCard(card)),
            local.Item("Fullscreen", () => SetFullscreen(card, !card.Fullscreen), check: card.Fullscreen),
        };
        if (HelpActionFor(card) is ({ } entry, { } help))
        {
            items.Add(local.Item("Help", () => RunDesktopAction(entry, help)));
        }
        else if (HelpUriFor(card) is { } uri)
        {
            items.Add(local.Item("Help", () => OpenUri(uri, "Help")));
        }

        local.Set([.. items]);
        return local;
    }

    private (DesktopEntry Entry, DesktopAction Action)? HelpActionFor(Card card)
    {
        if (card.AppId.Length == 0 || Desktop.FindForAppId(card.AppId) is not { } entry)
        {
            return null;
        }

        foreach (var action in entry.Actions)
        {
            if (action.Key.Contains("help", StringComparison.OrdinalIgnoreCase) ||
                action.Name.Contains("help", StringComparison.OrdinalIgnoreCase))
            {
                return (entry, action);
            }
        }

        return null;
    }

    private string? HelpUriFor(Card card) =>
        card.AppId.Length > 0 && Desktop.FindForAppId(card.AppId) is { } entry ? DocPath.UriFor(entry.Path) : null;

    private void RunDesktopAction(DesktopEntry entry, DesktopAction action)
    {
        if (entry.LaunchArgv(action, ExecLine.TerminalFromEnvironment()) is { Length: > 0 } argv)
        {
            Launch(ExecLine.Join(argv), action.Name, action.Icon ?? entry.Icon, entry.Id);
        }
        else
        {
            _report.Line($"LAUNCH cannot {entry.Id} {action.Key}");
        }
    }

    private void FillAppMenu(LunaView view, MenuSurface menu)
    {
        menu.Model.Rows.Clear();
        _menuItems.Clear();
        if (view.Deck.ActiveCard is not { } root)
        {
            return;
        }

        var card = Focused is { } focused && ReferenceEquals(focused.Parent ?? focused, root) ? focused : root;
        var source = MenuSourceFor(card);
        _menuSource = source;
        AddMenuRows(menu.Model.Rows, source, source.Menu.Items, 0);
        if (_config.Keyboard.Enabled)
        {
            if (menu.Model.Rows.Count > 0 && menu.Model.Rows[^1] is not DividerRow)
            {
                menu.Model.Rows.Add(new DividerRow());
            }

            menu.Model.Rows.Add(new ActionRow { Key = KeyboardMenuKey, Label = KeyboardMenuLabel, Detail = "", Enabled = true });
        }
    }

    private void AddMenuRows(System.Collections.ObjectModel.ObservableCollection<MenuRow> rows, IAppMenuSource source, IReadOnlyList<AppMenuItem> items, int depth)
    {
        foreach (var item in items)
        {
            if (!item.Visible)
            {
                continue;
            }

            if (item.Kind == AppMenuItemKind.Separator)
            {
                if (rows.Count > 0 && rows[^1] is not DividerRow)
                {
                    rows.Add(new DividerRow());
                }

                continue;
            }

            var key = $"m:{item.Id}";
            _menuItems[key] = item;
            var expanded = item.IsSubmenu && _expanded.Contains(item.Id);
            rows.Add(new ActionRow
            {
                Key = key,
                Label = item.Label,
                Detail = item.IsSubmenu ? (expanded ? "⌄" : "›") : ShortcutText(item.Shortcut),
                Checked = item.Kind is AppMenuItemKind.Check or AppMenuItemKind.Radio && item.ToggleState,
                Enabled = item.Enabled,
                Depth = depth,
            });
            if (expanded)
            {
                AddMenuRows(rows, source, item.Children, depth + 1);
            }
        }
    }

    private static string ShortcutText(AppMenuShortcut? shortcut)
    {
        if (shortcut is not { } value)
        {
            return "";
        }

        var text = new System.Text.StringBuilder();
        if ((value.Modifiers & AppMenuModifiers.Control) != 0)
        {
            text.Append("Ctrl+");
        }

        if ((value.Modifiers & AppMenuModifiers.Alt) != 0)
        {
            text.Append("Alt+");
        }

        if ((value.Modifiers & AppMenuModifiers.Shift) != 0)
        {
            text.Append("Shift+");
        }

        if ((value.Modifiers & AppMenuModifiers.Super) != 0)
        {
            text.Append("Super+");
        }

        return text.Append(value.Keysym.Length == 1 ? value.Keysym.ToUpperInvariant() : value.Keysym).ToString();
    }

    private const string KeyboardMenuKey = "k:keyboard";

    private void AppMenuRowChosen(LunaView view, MenuSurface menu, ActionRow row)
    {
        if (row.Key == KeyboardMenuKey)
        {
            CloseMenu(view);
            _report.Line($"APPMENU chose {KeyboardMenuLabel}");
            ToggleKeyboard();
            return;
        }

        if (_menuSource is not { } source || !_menuItems.TryGetValue(row.Key, out var item))
        {
            return;
        }

        if (item.IsSubmenu)
        {
            if (!_expanded.Remove(item.Id))
            {
                source.AboutToShow(item);
                _expanded.Add(item.Id);
            }

            FillAppMenu(view, menu);
            return;
        }

        if (!item.Enabled)
        {
            return;
        }

        CloseMenu(view);
        source.Activate(item, (uint)Environment.TickCount);
        _report.Line($"APPMENU chose {item.Label}");
    }

    private void PrintAppMenu()
    {
        var view = CommandView;
        if (view.InCardView || view.Deck.ActiveCard is not { } card)
        {
            _report.Line("APPMENU none");
            return;
        }

        var source = MenuSourceFor(Focused ?? card);
        _report.Line($"APPMENU {(source is LocalAppMenu ? "local" : source.Kind == AppMenuAddressKind.GMenu ? "gmenu" : "dbusmenu")} titles={source.Menu.Items.Count}");
        PrintMenuItems(source, source.Menu.Items, 1);
        if (_config.Keyboard.Enabled)
        {
            _report.Line("  -");
            _report.Line($"  {KeyboardMenuLabel}");
        }
    }

    private void PrintMenuItems(IAppMenuSource source, IReadOnlyList<AppMenuItem> items, int depth)
    {
        foreach (var item in items)
        {
            if (!item.Visible)
            {
                continue;
            }

            if (item.Kind == AppMenuItemKind.Separator)
            {
                _report.Line($"{new string(' ', depth * 2)}-");
                continue;
            }

            _report.Line($"{new string(' ', depth * 2)}{item.Label}{(item.Enabled ? "" : " (disabled)")}{(item.ToggleState ? " (checked)" : "")}");
            if (item.IsSubmenu)
            {
                source.AboutToShow(item);
                PrintMenuItems(source, item.Children, depth + 1);
            }
        }
    }

    private void AppMenuChoose(string path)
    {
        var view = CommandView;
        if (view.InCardView || view.Deck.ActiveCard is not { } card)
        {
            _report.Line("APPMENU none");
            return;
        }

        if (_config.Keyboard.Enabled && (path == "Show Keyboard" || path == "Hide Keyboard"))
        {
            _report.Line($"APPMENU chose {KeyboardMenuLabel}");
            ToggleKeyboard();
            return;
        }

        var source = MenuSourceFor(Focused ?? card);
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IReadOnlyList<AppMenuItem> level = source.Menu.Items;
        AppMenuItem? current = null;
        foreach (var part in parts)
        {
            current = null;
            foreach (var item in level)
            {
                if (item.Visible && string.Equals(AppMenuLabelsPlain(item.Label), part, StringComparison.OrdinalIgnoreCase))
                {
                    current = item;
                    break;
                }
            }

            if (current is null)
            {
                _report.Line($"APPMENU no {path}");
                return;
            }

            if (current.IsSubmenu)
            {
                source.AboutToShow(current);
                level = current.Children;
            }
        }

        if (current is null || current.IsSubmenu || !current.Enabled)
        {
            _report.Line($"APPMENU cannot {path}");
            return;
        }

        source.Activate(current, (uint)Environment.TickCount);
        _report.Line($"APPMENU chose {current.Label}");
    }

    private static string AppMenuLabelsPlain(string label) => label.Replace("_", "", StringComparison.Ordinal);

    private void StopAppMenu()
    {
        foreach (var card in _cards)
        {
            card.MenuImport?.Dispose();
            card.MenuImport = null;
        }

        _registrar?.Dispose();
        _registrar = null;
        _menuBus?.Dispose();
        _menuBus = null;
    }
}
