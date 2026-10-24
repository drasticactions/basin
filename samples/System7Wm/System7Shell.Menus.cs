using System7.Avalonia.Controls;
using System7.Avalonia.Rendering;
using Avalonia.Controls;
using Avalonia.Threading;
using Basin.Host;
using Basin.AppMenu;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Basin.Scene;
using Basin.Session;
using Basin;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private static readonly (string Name, DesktopMainCategory Category)[] CategoryNames =
    [
        ("Accessories", DesktopMainCategory.Utility),
        ("Development", DesktopMainCategory.Development),
        ("Education", DesktopMainCategory.Education),
        ("Games", DesktopMainCategory.Game),
        ("Graphics", DesktopMainCategory.Graphics),
        ("Internet", DesktopMainCategory.Network),
        ("Office", DesktopMainCategory.Office),
        ("Science", DesktopMainCategory.Science),
        ("Sound & Video", DesktopMainCategory.AudioVideo),
        ("System", DesktopMainCategory.System),
        ("Settings", DesktopMainCategory.Settings),
    ];

    private readonly DesktopEntries _desktopEntries = new();
    private readonly LocalAppMenu _finderMenus = new();
    private readonly HashSet<uint> _consumedKeys = [];
    private MenuBar? _menuBar;
    private IconCache? _iconCache;
    private ShellClock? _clock;
    private string _finderView = "icon";

    internal MenuBar? Bar => _menuBar;

    private IconCache Icons => _iconCache ??= new IconCache(_desktopEntries);

    private void AttachMenus()
    {
        _menuBar = new MenuBar(_layers.MenuBar, _ui, _chrome) { Depth = _config.Depth };
        _menuBar.Opened += () => BasinReport.Line("MENUBAR open");
        _menuBar.Shrunk += () =>
        {
            BasinReport.Line("MENUBAR closed");
            _seat.Refresh();
            _outputs.ScheduleAll();
        };
        _menuBar.Resized += _outputs.ScheduleAll;
        _desktopEntries.Watch(Watch);
        _desktopEntries.Changed += () =>
        {
            _iconCache?.Clear();
            BuildApple();
            _outputs.ScheduleAll();
        };
        BuildApple();
        BuildFinderMenus();
        RefreshMenus();
    }

    private void RelayoutChrome()
    {
        if (MainView is { } view && _menuBar is { } bar)
        {
            var box = _layout.BoxOf(view.Output);
            bar.Place(box.X, box.Y, view.Output.Scale, box, DevicePlacement.OutputSize(view.Output));
        }

        UpdateAllFrames();
        RelayoutDesktop();
    }

    private void ApplyDepth()
    {
        if (_menuBar is { } bar)
        {
            bar.Depth = _config.Depth;
        }

        foreach (var window in _stack)
        {
            window.Frame?.SetDepth(_config.Depth);
        }

        ApplyDesktopDepth();
        ApplyApplicationDepth();
        ApplyTerminalDepth();
        RefreshApplicationMenu();
    }

    internal IAppMenuSource FinderMenus => _finderMenus;

    private void RefreshMenus()
    {
        if (_menuBar is not { } bar)
        {
            return;
        }

        var front = _front ?? _finder;
        bar.App = front.IsFinder ? _finderMenus : front.MenusFor(ActiveOf(front));
        RefreshApplicationMenu();
    }

    private void RefreshApplicationMenu()
    {
        if (_menuBar is not { } bar)
        {
            return;
        }

        var front = _front ?? _finder;
        var menu = bar.Application;
        menu.Clear();
        var items = new List<AppMenuItem>
        {
            menu.Item($"Hide {front.Name}", front.IsFinder ? null : () => HideApp(front)),
            menu.Item("Hide Others", () => HideOthers(front)),
            menu.Item("Show All", ShowAll, enabled: _apps.Values.Any(a => a.Hidden)),
            menu.Separator(),
        };
        if (DensityTarget(front) is { } x11)
        {
            var low = _xwaylandModule?.DisplayOf(x11)?.Density == X11Density.Low;
            items.Add(menu.Item(low ? "Use Full Density" : "Use Low Density", () => ToggleX11Density(x11)));
            items.Add(menu.Separator());
        }
        foreach (var app in _apps.Values.OrderBy(a => a.IsFinder ? "" : a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var target = app;
            items.Add(menu.Item(app.Name, () => SwitchTo(target), check: ReferenceEquals(app, front)));
        }

        menu.Set(menu.Submenu("Application", items));
        var icon = (front.Icon ?? System7IconFamily.Generic("application"))?.Small;
        var depth = _config.Depth;
        bar.ApplicationTitle = () =>
        {
            var view = new System7IconView { Icon = icon, Width = 16, Height = 16 };
            System7.Avalonia.System7Theme.SetColorDepth(view, depth);
            return view;
        };
        bar.Rebuild();
    }

    private void BuildApple()
    {
        if (_menuBar is not { } bar)
        {
            return;
        }

        var menu = bar.Apple;
        menu.Clear();
        var items = new List<AppMenuItem>
        {
            menu.Item("About This Macintosh…", OpenAbout),
            menu.Separator(),
            menu.Item("Terminal", NewTerminal),
            menu.Submenu("Control Panels", ControlPanels.Names.Select(name => menu.Item(name, () => OpenPanel(name))), iconName: "control-panels-folder"),
            menu.Submenu("Applications", ApplicationItems(menu)),
        };
        items.AddRange(AppleMenuFolder(menu, Path.Combine(XdgDirectories.ConfigHome, "system7-wm", "Apple Menu Items"), 0));
        menu.Set(menu.Submenu("\u0014", items));
        bar.Rebuild();
    }

    private IEnumerable<AppMenuItem> ApplicationItems(LocalAppMenu menu)
    {
        var entries = _desktopEntries.Listable();
        var groups = new List<AppMenuItem>();
        foreach (var (name, category) in CategoryNames)
        {
            var members = entries.Where(e => DesktopCategories.MainOf(e) == category).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (members.Count > 0)
            {
                groups.Add(menu.Submenu(name, members.Select(entry => menu.Item(entry.Name, () => Launch(entry)))));
            }
        }

        var other = entries.Where(e => DesktopCategories.MainOf(e) == DesktopMainCategory.Other).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (other.Count > 0)
        {
            groups.Add(menu.Submenu("Other", other.Select(entry => menu.Item(entry.Name, () => Launch(entry)))));
        }

        return groups;
    }

    private IEnumerable<AppMenuItem> AppleMenuFolder(LocalAppMenu menu, string folder, int depth)
    {
        if (depth > 4 || !Directory.Exists(folder))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(folder).OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.'))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                yield return menu.Submenu(name, AppleMenuFolder(menu, path, depth + 1).ToList());
            }
            else if (path.EndsWith(".desktop", StringComparison.Ordinal) && DesktopEntryReader.Parse(path, name, DesktopLocale.None) is { } entry)
            {
                var target = entry;
                yield return menu.Item(entry.Name, () => Launch(target));
            }
            else
            {
                var file = path;
                yield return menu.Item(Path.GetFileNameWithoutExtension(name), () => OpenPath(file));
            }
        }
    }

    internal void Launch(DesktopEntry entry)
    {
        if (entry.Terminal && TerminalCommand() is null)
        {
            if (entry.Argv is { Count: > 0 } own)
            {
                OpenTerminal(own, entry.Name, entry.WorkingDirectory);
            }

            return;
        }

        if (entry.LaunchArgv(TerminalCommand()) is not { Length: > 0 } argv)
        {
            return;
        }

        Spawn(ExecLine.Join(argv), entry.Id);
    }

    internal void OpenPath(string path)
    {
        if (path.EndsWith(".desktop", StringComparison.Ordinal) && DesktopEntryReader.Parse(path, Path.GetFileName(path), DesktopLocale.None) is { } entry)
        {
            Launch(entry);
            return;
        }

        Spawn($"xdg-open {ExecLine.Join([path])}");
    }

    private void BuildSynthesized(AppGroup app)
    {
        var menu = app.Synthesized;
        menu.Clear();
        var facts = app.Windows.Count > 0 ? app.Windows[^1].Facts : new Basin.Config.WindowFacts { AppId = app.AppId };
        var chords = _config.RuleFor(facts)?.EditChords ?? Basin.Config.Modifiers.Ctrl;
        void Edit(char letter) => SendEditChord(chords, letter);
        menu.Set(
            menu.Submenu(app.Name, [menu.Item($"About {app.Name}…", () => AboutApp(app))]),
            menu.Submenu("File",
            [
                menu.Item("Close Window", () => (_active is { } w && ReferenceEquals(w.App, app) ? w : app.Windows.LastOrDefault())?.Close(), 'W'),
                menu.Separator(),
                menu.Item("Quit", () => QuitApp(app), 'Q'),
            ]),
            menu.Submenu("Edit",
            [
                menu.Item("Undo", () => Edit('z'), 'Z'),
                menu.Separator(),
                menu.Item("Cut", () => Edit('x'), 'X'),
                menu.Item("Copy", () => Edit('c'), 'C'),
                menu.Item("Paste", () => Edit('v'), 'V'),
                menu.Item("Select All", () => Edit('a'), 'A'),
            ]));
    }

    private void QuitApp(AppGroup app)
    {
        foreach (var window in app.Windows.ToList())
        {
            window.Close();
        }
    }

    private void SendEditChord(Basin.Config.Modifiers modifiers, char letter)
    {
        var keyboard = Seat.Keyboard;
        if (keyboard.Keymap is not { } keymap || !keyboard.TryKeycodeForKeysym(letter, out var code, out _))
        {
            return;
        }

        var mask = 0u;
        if (modifiers.HasFlag(Basin.Config.Modifiers.Ctrl) && keymap.GetModIndex("Control") is { } control)
        {
            mask |= 1u << (int)control;
        }

        if (modifiers.HasFlag(Basin.Config.Modifiers.Shift) && keymap.GetModIndex("Shift") is { } shift)
        {
            mask |= 1u << (int)shift;
        }

        var (depressed, latched, locked, group) = keyboard.ModifierState;
        var time = (uint)Environment.TickCount;
        keyboard.NotifyModifiers(depressed | mask, latched, locked, group);
        keyboard.SendKey(time, code, Wayland.WlKeyboard.KeyState.Pressed);
        keyboard.SendKey(time, code, Wayland.WlKeyboard.KeyState.Released);
        keyboard.NotifyModifiers(depressed, latched, locked, group);
    }

    private void BuildFinderMenus()
    {
        var menu = _finderMenus;
        menu.Clear();
        var emptyTrash = menu.Item("Empty Trash…", AskEmptyTrash, enabled: !Trash.IsEmpty);
        var cleanUp = menu.Item(ActiveFinderView is { IsSpatial: true } ? "Clean Up Window" : "Clean Up Desktop", CleanUp);
        var mode = ActiveFinderView?.Mode ?? _finderView;
        var views = new[]
        {
            (Mode: "small", Item: menu.Item("by Small Icon", () => SetFinderView("small"), check: mode == "small")),
            (Mode: "icon", Item: menu.Item("by Icon", () => SetFinderView("icon"), check: mode == "icon")),
            (Mode: "name", Item: menu.Item("by Name", () => SetFinderView("name"), check: mode == "name")),
        };
        menu.Set(
            menu.Submenu("File",
            [
                menu.Item("New Folder", NewFolder, 'N'),
                menu.Item("Open", OpenSelection, 'O'),
                menu.Item("Print", null, 'P'),
                menu.Item("Close Window", CloseFinderWindow, 'W'),
                menu.Separator(),
                menu.Item("Get Info", null, 'I'),
                menu.Item("Duplicate", Duplicate, 'D'),
                menu.Item("Make Alias", null, 'M'),
                menu.Item("Put Away", PutAway, 'Y'),
                menu.Separator(),
                menu.Item("Find…", null, 'F'),
                menu.Item("Find Again", null, 'G'),
            ]),
            menu.Submenu("Edit",
            [
                menu.Item("Undo", null, 'Z'),
                menu.Separator(),
                menu.Item("Cut", null, 'X'),
                menu.Item("Copy", null, 'C'),
                menu.Item("Paste", null, 'V'),
                menu.Item("Clear", null),
                menu.Item("Select All", SelectAll, 'A'),
                menu.Separator(),
                menu.Item("Show Clipboard", null),
            ]),
            menu.Submenu("View", views.Select(v => v.Item), opening: () =>
            {
                var current = ActiveFinderView?.Mode ?? _finderView;
                var changed = false;
                foreach (var (viewMode, item) in views)
                {
                    changed |= item.ToggleState != (viewMode == current);
                    item.ToggleState = viewMode == current;
                }

                if (changed)
                {
                    menu.Menu.NotifyChanged();
                }
            }),
            menu.Submenu("Special",
            [
                cleanUp,
                emptyTrash,
                menu.Separator(),
                menu.Item("Eject Disk", null, 'E'),
                menu.Item("Erase Disk…", null),
                menu.Separator(),
                menu.Item("Sleep", () => AskPower(LogindAction.Suspend)),
                menu.Item("Restart", () => AskPower(LogindAction.Reboot)),
                menu.Item("Shut Down", () => AskPower(LogindAction.PowerOff)),
                menu.Separator(),
                menu.Item("Log Out", AskLogOut),
            ], opening: () =>
            {
                var full = !Trash.IsEmpty;
                var label = ActiveFinderView is { IsSpatial: true } ? "Clean Up Window" : "Clean Up Desktop";
                if (emptyTrash.Enabled != full || cleanUp.Label != label)
                {
                    emptyTrash.Enabled = full;
                    cleanUp.Label = label;
                    menu.Menu.NotifyChanged();
                }
            }));
        _menuBar?.Rebuild();
    }

    private void SetFinderView(string view)
    {
        _finderView = view;
        ApplyFinderView(view);
        BuildFinderMenus();
    }

    private void StartClock()
    {
        _clock = new ShellClock(
            _host.Loop,
            now => _config.Clock ? now.ToString(_config.ClockFormat, System.Globalization.CultureInfo.CurrentCulture) : null,
            text => _menuBar?.SetClock(text));
        _clock.Refresh();
    }

    private void StopClock()
    {
        _clock?.Dispose();
        _clock = null;
    }

    internal Basin.Config.Modifiers HeldModifiers => Basin.Config.HeldModifiers.From(Seat.Keyboard.State);

    private bool OnKey(uint time, uint key, bool pressed)
    {
        if (pressed && !_locked && TryInspectorChord(key))
        {
            _consumedKeys.Add(key);
            return true;
        }

        if (!_consumedKeys.Contains(key) && _inspector?.Input.Key(time, key, pressed) == true)
        {
            return true;
        }

        if (!pressed)
        {
            return _consumedKeys.Remove(key);
        }

        if (_locked)
        {
            return false;
        }

        if (HeldModifiers == (Basin.Config.Modifiers.Mod4 | Basin.Config.Modifiers.Mod1) &&
            Basin.Accessibility.DesktopAccessibility.DefaultActionForKey(key) is { } action &&
            _access?.Run(action) == true)
        {
            _consumedKeys.Add(key);
            return true;
        }

        if (TryTerminalChord(key))
        {
            _consumedKeys.Add(key);
            return true;
        }

        if ((HeldModifiers & _config.Command) == 0 || _menuBar is not { } bar)
        {
            return false;
        }

        var symbol = Seat.Keyboard.RawKeysymFor(key).Value;
        if (symbol is < 0x20 or > 0x7e)
        {
            return false;
        }

        var keysym = char.ToLowerInvariant((char)symbol).ToString();
        if (!bar.TryShortcut(keysym, time))
        {
            return false;
        }

        BasinReport.Line($"CHORD {keysym}");
        _consumedKeys.Add(key);
        return true;
    }

    private ShellWindow? ShellChromeOwner(IUISurface surface)
    {
        foreach (var window in _stack)
        {
            if (ReferenceEquals(window.ShellSurface?.Surface, surface))
            {
                return window;
            }

            if (window.Frame is { } frame &&
                (ReferenceEquals(frame.Strip.Surface, surface) || ReferenceEquals(frame.GrowBox.Surface, surface)))
            {
                return window;
            }
        }

        return null;
    }

    internal System7IconFamily IconFor(string? name, string generic, bool appId = false) => Icons.Family(name, generic, appId);

    internal void Beep()
    {
        var sound = _config.AlertSound;
        if (Path.IsPathRooted(sound) && File.Exists(sound))
        {
            PlaySound(sound, Path.GetFileNameWithoutExtension(sound));
            return;
        }

        var alert = System7AlertSound.Find(sound) ?? System7AlertSound.SimpleBeep;
        if (AlertWav(alert) is { } wav)
        {
            PlaySound(wav, alert.Name);
        }
    }

    private string? AlertWav(System7AlertSound alert)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } dir ? dir : Path.GetTempPath();
        var path = Path.Combine(runtime, "system7-wm", $"alert-{alert.Name.Replace(' ', '-')}.wav");
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                File.WriteAllBytes(temporary, alert.ToWav());
                File.Move(temporary, path, overwrite: true);
            }

            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn(error, $"sound: cannot write the alert sound to {path}");
            return null;
        }
    }

    private bool _noPlayerLogged;

    private static bool Muted => Environment.GetEnvironmentVariable("BASIN_MUTE") is { Length: > 0 } and not "0";

    private void PlaySound(string path, string name)
    {
        if (Muted)
        {
            BasinReport.Line($"BEEP {name}");
            return;
        }

        if ((string[])["pw-play", "paplay", "aplay"] is var players && players.FirstOrDefault(OnPath) is not { } player)
        {
            if (!_noPlayerLogged)
            {
                _noPlayerLogged = true;
                _log.Warn($"sound: no pw-play, paplay or aplay on PATH, alert sounds do not play");
            }

            return;
        }

        Spawn($"{player} {ExecLine.Join([path])}");
        BasinReport.Line($"BEEP {name}");
    }

    private void AboutApp(AppGroup app)
    {
        var entry = app.AppId.Length > 0 ? _desktopEntries.FindForAppId(app.AppId) : null;
        ShowAlert(app.Name, entry?.Comment ?? entry?.GenericName ?? app.AppId, ["OK"], _ => { });
    }
}
