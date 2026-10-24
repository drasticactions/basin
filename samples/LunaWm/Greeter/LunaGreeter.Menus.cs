using System.Collections.ObjectModel;
using Basin;
using Basin.Diagnostics;
using Basin.Greetd;
using Basin.Session;

namespace LunaWm;

internal sealed partial class LunaGreeter
{
    private static readonly double[] TextScales = [1.0, 1.25, 1.5, 2.0];

    private StatusBar? _bar;
    private MenuSurface? _menu;
    private LunaStatus? _indicators;
    private string? _armed;

    private void WireStatus()
    {
        _bar = new StatusBar(_barLayer, _ui, _index);
        _bar.Model.Locked = true;
        _bar.Model.Pressed += OnBarPressed;
        _indicators = new LunaStatus(_host.Loop, _report, _log, "luna-wm-greeter") { JoinAllowed = _config.Settings.NetworkJoin };
        _indicators.Changed += OnStatusChanged;
        _indicators.WifiMenusChanged += () =>
        {
            if (_menu is { Open: true, Kind: "wifi" })
            {
                FillMenu(rebuild: true);
                if (_indicators.WifiAsking && _menu.Chrome.Surface is { } surface)
                {
                    _seat.Router.SetKeyboardFocus(surface);
                }
            }
        };
        _indicators.Start(null, _config.Settings.Battery, _config.Settings.Network, bluetooth: true, sound: true);
        PlaceChrome();
    }

    private void OnStatusChanged()
    {
        if (_bar is { } bar && _indicators is { } status)
        {
            status.Sync(bar.Model);
        }

        if (_menu is { Open: true })
        {
            FillMenu(rebuild: false);
        }
    }

    private void OnBarPressed(string what)
    {
        _report.Line($"BARPRESS {what}");
        if (what is "battery" or "wifi" or "bluetooth" or "sound" or "device")
        {
            ToggleMenu(what);
        }
    }

    private void ToggleMenu(string kind)
    {
        if (_menu is { Open: true } menu && menu.Kind == kind)
        {
            CloseMenu();
            return;
        }

        OpenMenu(kind);
    }

    internal void OpenMenu(string kind)
    {
        if (kind is not ("battery" or "wifi" or "bluetooth" or "sound" or "device" or "session"))
        {
            _report.Line($"MENU unknown {kind}");
            return;
        }

        if (_menu is null)
        {
            _menu = new MenuSurface(_menuLayer, _ui, _index);
            _menu.Model.Dismissed += CloseMenu;
            _menu.Model.Chosen += MenuChosen;
            _menu.Frame.Alpha = 1;
        }

        _menu.Kind = kind;
        _menu.Model.Kind = kind;
        _armed = null;
        FillMenu(rebuild: true);
        _menu.Open = true;
        PlaceChrome();
        _menu.Chrome.Enabled = true;
        _menu.Chrome.InputEnabled = true;
        _outputs.ScheduleAll();
        _report.Line($"MENU {kind} on");
    }

    internal void CloseMenu()
    {
        if (_menu is not { Open: true } menu)
        {
            return;
        }

        menu.Open = false;
        menu.Chrome.InputEnabled = false;
        menu.Chrome.Enabled = false;
        if (_box.Surface is { } box)
        {
            _seat.Router.SetKeyboardFocus(box);
        }

        _outputs.ScheduleAll();
        _report.Line($"MENU {menu.Kind} off");
    }

    private void PlaceChrome()
    {
        if (_active.Current is not { } output)
        {
            return;
        }

        var box = _layout.BoxOf(output);
        _bar?.Chrome.Place(new Box(box.X, box.Y, box.Width, LunaView.StatusBarHeight), output.Scale);
        if (_menu is { } menu)
        {
            menu.Model.PanelWidth = LunaShell.MenuWidth;
            menu.Model.PanelLeft = menu.Kind == "session"
                ? Math.Max(0, (box.Width - LunaShell.MenuWidth) / 2.0)
                : Math.Max(0, box.Width - LunaShell.MenuWidth);
            menu.Place(new Box(box.X, box.Y + LunaView.StatusBarHeight, box.Width, Math.Max(1, box.Height - LunaView.StatusBarHeight)), output.Scale);
        }
    }

    private void FillMenu(bool rebuild)
    {
        if (_menu is not { } menu || _active.Current is not { } output || _indicators is not { } status)
        {
            return;
        }

        var rows = menu.Model.Rows;
        if (!rebuild && rows.Count > 0 && menu.Kind is "sound" or "battery")
        {
            status.SyncSliders(menu.Kind, rows, output);
            return;
        }

        rows.Clear();
        if (menu.Kind == "device")
        {
            FillDevice(rows);
            return;
        }

        if (menu.Kind == "session")
        {
            FillSessions(rows);
            return;
        }

        status.Fill(menu.Kind, rows, output, AddPowerRows);
    }

    private void FillDevice(ObservableCollection<MenuRow> rows)
    {
        rows.Add(new HeaderRow { Label = "Device", Detail = Environment.MachineName });

        if (_flow is { } layouts && _config.Settings.Layouts.Count > 1)
        {
            rows.Add(new DividerRow());
            rows.Add(new HeaderRow { Label = "Keyboard layout" });
            foreach (var layout in _config.Settings.Layouts)
            {
                var id = layout;
                rows.Add(new ActionRow
                {
                    Key = $"layout:{id}",
                    Label = id,
                    Checked = layouts.SelectedLayout == id,
                    Invoked = () => layouts.SelectLayout(id, byUser: true),
                });
            }
        }

        rows.Add(new DividerRow());
        rows.Add(new HeaderRow { Label = "Accessibility" });
        rows.Add(new ToggleRow { Key = "contrast", Label = "High contrast", On = _highContrast, Changed = SetHighContrast });
        foreach (var scale in TextScales)
        {
            var chosen = scale;
            rows.Add(new ActionRow
            {
                Key = $"text:{SizeOf(chosen)}",
                Label = $"Text size {SizeOf(chosen)}",
                Checked = Math.Abs(_textScale - chosen) < 0.001,
                Invoked = () => SetTextScale(chosen),
            });
        }

        if (_reader is { IsAvailable: true } reader)
        {
            rows.Add(new ToggleRow
            {
                Key = "reader",
                Label = "Screen reader",
                On = reader.IsRunning,
                Changed = on =>
                {
                    if (on)
                    {
                        reader.Start();
                    }
                    else
                    {
                        reader.Stop();
                    }
                },
            });
        }

        rows.Add(new DividerRow());
        AddPowerRows(rows);
    }

    private void FillSessions(ObservableCollection<MenuRow> rows)
    {
        rows.Add(new HeaderRow { Label = "Session" });
        if (_flow is not { } flow)
        {
            return;
        }

        foreach (var session in flow.Sessions)
        {
            var id = session.Id;
            rows.Add(new ActionRow
            {
                Key = $"session:{id}",
                Label = session.Name,
                Detail = session.Type == GreetdSessionType.X11 ? "X11" : "",
                Checked = flow.SelectedSession == id,
                Invoked = () => flow.SelectSession(id, byUser: true),
            });
        }
    }

    private void AddPowerRows(ObservableCollection<MenuRow> rows)
    {
        if (!_config.Settings.Power)
        {
            return;
        }

        rows.Add(new ActionRow { Key = "restart", Label = "Restart" });
        rows.Add(new ActionRow { Key = "shutdown", Label = "Shut Down" });
    }

    private void MenuChosen(MenuRow row)
    {
        if (_menu is not { } menu)
        {
            return;
        }

        _report.Line($"MENUCHOSE {menu.Kind} {(row.Key.Length > 0 ? row.Key : row.Label)}");
        switch (row)
        {
            case ActionRow { Key: "restart" or "shutdown" } power:
                if (_armed != power.Key)
                {
                    _armed = power.Key;
                    power.Label = power.Key == "restart" ? "Tap again to restart" : "Tap again to shut down";
                    return;
                }

                _armed = null;
                CloseMenu();
                RunPower(power.Key == "restart" ? LogindAction.Reboot : LogindAction.PowerOff);
                return;
            case ActionRow when menu.Kind == "session":
                CloseMenu();
                return;
            case ActionRow or ToggleRow when menu.Kind == "device":
                FillMenu(rebuild: true);
                return;
        }
    }

    private void RunPower(LogindAction action)
    {
        _report.Line($"POWER {action.ToString().ToLowerInvariant()}");
        if (Environment.GetEnvironmentVariable("LUNAWM_NO_POWER") == "1" || !LogindActions.Available)
        {
            _log.Info($"power: {action} skipped, no logind or LUNAWM_NO_POWER is set");
            return;
        }

        _ = LogindActions.RunAsync(action);
    }

    internal bool ChooseMenuRow(string key)
    {
        if (_menu is not { Open: true } menu || menu.Model.Rows.FirstOrDefault(row => row.Key == key) is not { } row)
        {
            return false;
        }

        switch (row)
        {
            case ToggleRow toggle:
                toggle.Toggle();
                break;
            case ActionRow action:
                action.Invoked?.Invoke();
                break;
        }

        menu.Model.Choose(row);
        return true;
    }

    private void PrintMenuState()
    {
        if (_menu is not { Open: true } menu)
        {
            _report.Line("  menu closed");
            return;
        }

        _report.Line($"  menu {menu.Kind} rows={menu.Model.Rows.Count}");
        foreach (var row in menu.Model.Rows)
        {
            var text = row switch
            {
                HeaderRow header => $"header {header.Label} | {header.Detail}",
                ToggleRow toggle => $"toggle {toggle.Key} {toggle.State}",
                SliderRow slider => $"slider {slider.Label} {slider.Value:0.##}",
                ActionRow action => $"action {action.Key} {action.Label}{(action.Checked ? " checked" : "")}",
                NoteRow note => $"note {note.Label}",
                PassphraseRow passphrase => $"passphrase {passphrase.Label}",
                _ => "divider",
            };
            _report.Line($"  row {text}");
        }
    }

    private static string SizeOf(double scale) => $"{Math.Round(scale * 100).ToString(System.Globalization.CultureInfo.InvariantCulture)}%";
}
