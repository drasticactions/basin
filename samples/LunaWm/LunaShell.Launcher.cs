using Avalonia.Media;
using Basin;
using Basin.Freedesktop;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private static readonly uint[] TileColors = [0xff2d6bb3, 0xff3a8a3a, 0xffb3532d, 0xff7a3ab3, 0xff2d9aa3, 0xffa38a2d, 0xffb32d6b];

    private readonly LunaIcons _icons = new();
    private readonly List<LauncherEntry> _entries = [];
    private LauncherState _launcherState = null!;
    private DirectoryWatch? _desktopWatch;

    private void StartLauncher()
    {
        _desktopWatch = new DirectoryWatch(_host.Loop);
        Desktop.Watch(_desktopWatch);
        Desktop.Changed += () =>
        {
            LoadLauncher();
            _report.Line($"LAUNCHER entries={_entries.Count}");
        };
        LoadLauncher();
    }

    private void StopLauncher()
    {
        Desktop.Watch(null);
        _desktopWatch?.Dispose();
        _desktopWatch = null;
        _icons.Dispose();
    }

    private void LoadLauncher()
    {
        _entries.Clear();
        foreach (var entry in Desktop.Listable(CurrentDesktopNames))
        {
            _entries.Add(new LauncherEntry(entry.Id, entry.Name, entry.Icon, entry.Categories, entry.GenericName, entry.Keywords));
        }

        var saved = LauncherState.Read(LauncherState.DefaultPath(), out var error);
        if (error is not null)
        {
            _log.Warn(error, $"launcher: cannot read the state {LauncherState.DefaultPath()}, the seed is used");
        }

        _launcherState = LauncherState.Build(_config.Tabs, _config.QuickLaunch, _entries, saved);
        foreach (var view in _views)
        {
            FillLauncher(view);
        }
    }

    private static IReadOnlySet<string> CurrentDesktopNames { get; } = new HashSet<string>(StringComparer.Ordinal) { "luna", "basin" };

    private void SaveLauncher()
    {
        if (!_launcherState.Write(LauncherState.DefaultPath(), out var error))
        {
            _log.Warn(error, $"launcher: cannot save the state to {LauncherState.DefaultPath()}");
        }
    }

    private LauncherEntry? EntryFor(string id)
    {
        foreach (var entry in _entries)
        {
            if (entry.Id == id)
            {
                return entry;
            }
        }

        return null;
    }

    private LauncherIconModel IconModel(LauncherEntry entry) => new()
    {
        Id = entry.Id,
        Name = entry.Name,
        Icon = _icons.Load(entry.Icon, 64),
        Tile = new SolidColorBrush(Color.FromUInt32(TileColors[(int)((uint)entry.Id.GetHashCode(StringComparison.Ordinal) % TileColors.Length)])),
    };

    private void AttachLauncher(LunaView view)
    {
        var launcher = new LauncherModel();
        launcher.Launched += icon => LaunchEntry(view, icon.Id);
        launcher.Moved += (icon, tab, position) =>
        {
            _launcherState.Move(icon.Id, tab, position);
            SaveLauncher();
            _report.Line($"LAUNCHER move {icon.Id} {tab} {position}");
        };
        launcher.Hidden += icon =>
        {
            _launcherState.Hide(icon.Id);
            SaveLauncher();
            _report.Line($"LAUNCHER hide {icon.Id}");
        };
        launcher.Docked += (slot, icon) =>
        {
            _launcherState.SetQuickLaunch(slot, icon.Id);
            SaveLauncher();
            FillDocks();
            _report.Line($"LAUNCHER dock {slot} {icon.Id}");
        };
        launcher.ReorderingChanged += on => _report.Line($"LAUNCHER reorder {(on ? "on" : "off")}");
        launcher.SearchRequested += () => OpenJustType(view, "");
        view.LauncherUi = new LauncherSurface(view.Launcher, _ui, _chromeIndex, launcher);
        view.LauncherUi.Fade.Jump(0);
        view.LauncherUi.Frame.Alpha = 0;

        var dock = new QuickLaunchModel();
        dock.Launched += icon => LaunchEntry(view, icon.Id);
        dock.LauncherRequested += () => ShowLauncher(view, true);
        view.Dock = new DockSurface(view.QuickLaunch, _ui, _chromeIndex, dock);
        view.Dock.Slide.Jump(0);
        FillLauncher(view);
        PlaceLauncher(view);
        SyncDock(view, animate: false);
    }

    private void FillLauncher(LunaView view)
    {
        if (view.LauncherUi is not { } ui || _launcherState is null)
        {
            return;
        }

        var model = ui.Model;
        var selected = model.Current?.Name;
        model.Tabs.Clear();
        foreach (var (name, apps) in _launcherState.Tabs)
        {
            var tab = new LauncherTabModel { Name = name };
            foreach (var id in apps)
            {
                if (!_launcherState.Hidden.Contains(id) && EntryFor(id) is { } entry)
                {
                    tab.Icons.Add(IconModel(entry));
                }
            }

            model.Tabs.Add(tab);
        }

        model.Current = null;
        if (selected is not null)
        {
            model.Select(selected);
        }

        model.Current ??= model.Tabs.Count > 0 ? model.Tabs[0] : null;
        FillDock(view);
    }

    private void FillDocks()
    {
        foreach (var view in _views)
        {
            FillDock(view);
        }
    }

    private void FillDock(LunaView view)
    {
        if (_launcherState is null)
        {
            return;
        }

        var icons = new List<LauncherIconModel>();
        foreach (var id in _launcherState.QuickLaunch)
        {
            if (EntryFor(id) is { } entry)
            {
                icons.Add(IconModel(entry));
            }
        }

        foreach (var target in new[] { view.Dock?.Model.Icons, view.LauncherUi?.Model.Dock })
        {
            if (target is null)
            {
                continue;
            }

            target.Clear();
            foreach (var icon in icons)
            {
                target.Add(icon);
            }
        }
    }

    private void PlaceLauncher(LunaView view)
    {
        view.LauncherUi?.Chrome.Place(new Box(0, LunaView.StatusBarHeight, view.Box.Width, Math.Max(1, view.Box.Height - LunaView.StatusBarHeight)), view.Scale);
        if (view.Dock is { } dock)
        {
            dock.Chrome.Place(new Box(0, view.Box.Height - (int)QuickLaunchModel.Height, view.Box.Width, (int)QuickLaunchModel.Height), view.Scale);
            ApplyDockSlide(dock);
        }
    }

    internal bool LauncherOpen(LunaView view) => view.LauncherUi is { Open: true };

    internal void ShowLauncher(LunaView view, bool open)
    {
        if (view.LauncherUi is not { } ui || ui.Open == open)
        {
            return;
        }

        if (open)
        {
            CloseOtherShellUi(view, keepMenu: false);
            ui.Model.Reordering = false;
            if (ui.Model.Tabs.Count > 0)
            {
                ui.Model.Current = ui.Model.Tabs[0];
            }
        }

        ui.Open = open;
        ui.Chrome.Enabled = true;
        ui.Chrome.InputEnabled = open;
        if (open)
        {
            TakeChromeKeyboard(ui.Chrome);
        }
        else
        {
            ReleaseChromeKeyboard(ui.Chrome);
        }
        ui.Fade.Start(LunaMotion.Launcher, ui.Fade.Value, open ? 1 : 0, Now, 1);
        ui.Frame.Alpha = (float)ui.Fade.Value;
        SyncDock(view, animate: true);
        view.Scheduler?.ScheduleRepaint();
        _seat.Refresh();
        _report.Line($"LAUNCHER {(open ? "on" : "off")}");
    }

    internal bool QuickLaunchOpen(LunaView view) => view.Dock is { Shown: true } && !view.InCardView;

    private bool _dockRequested;

    internal void ShowQuickLaunch(LunaView view, bool open, bool follow = false)
    {
        if (view.Dock is not { } dock)
        {
            return;
        }

        _dockRequested = open;
        dock.Following = open && follow;
        SyncDock(view, animate: !follow);
        if (open && !follow && dock.Shown)
        {
            TakeChromeKeyboard(dock.Chrome);
        }
        _report.Line($"QUICKLAUNCH {(open ? "on" : "off")}");
    }

    private void SyncDock(LunaView view, bool animate)
    {
        if (view.Dock is not { } dock)
        {
            return;
        }

        var shown = !LauncherOpen(view) && !JustTypeOpen(view) && !_locked && (view.InCardView || _dockRequested);
        if (shown == dock.Shown && !dock.Following)
        {
            return;
        }

        dock.Shown = shown;
        dock.Chrome.Enabled = true;
        dock.Chrome.InputEnabled = shown;
        if (!shown)
        {
            ReleaseChromeKeyboard(dock.Chrome);
        }
        if (dock.Following)
        {
            return;
        }

        if (animate)
        {
            dock.Slide.Start(LunaMotion.QuickLaunch, dock.Slide.Value, shown ? 1 : 0, Now, 1);
        }
        else
        {
            dock.Slide.Jump(shown ? 1 : 0);
        }

        ApplyDockSlide(dock);
        view.Scheduler?.ScheduleRepaint();
    }

    private static void ApplyDockSlide(DockSurface dock)
    {
        dock.Frame.Matrix = RenderTransform.Translation(0, (1 - dock.Slide.Value) * QuickLaunchModel.Height);
        dock.Frame.Alpha = dock.Slide.Value <= 0 ? 0 : 1;
    }

    private void DockFollow(LunaView view, double x, double y)
    {
        if (view.Dock is not { } dock)
        {
            return;
        }

        dock.Following = true;
        dock.Shown = true;
        dock.Chrome.Enabled = true;
        var reveal = Math.Clamp((view.Box.Height - y) / QuickLaunchModel.Height, 0, 1);
        dock.Slide.Jump(Math.Max(dock.Slide.Value, reveal));
        ApplyDockSlide(dock);
        dock.Model.Highlight = y >= view.Box.Height - QuickLaunchModel.Height - 40 ? dock.Model.SlotAt(x, view.Box.Width) : -1;
        view.Scheduler?.ScheduleRepaint();
    }

    private void DockRelease(LunaView view, double x, double y)
    {
        if (view.Dock is not { } dock)
        {
            return;
        }

        dock.Following = false;
        dock.Model.Highlight = -1;
        if (double.IsNaN(x))
        {
            ShowQuickLaunch(view, false);
            return;
        }

        dock.Slide.Start(LunaMotion.QuickLaunch, dock.Slide.Value, 1, Now, 1);
        dock.Chrome.InputEnabled = true;
        view.Scheduler?.ScheduleRepaint();
        if (y < view.Box.Height - QuickLaunchModel.Height - 40)
        {
            _report.Line("QUICKLAUNCH open");
            return;
        }

        var slot = dock.Model.SlotAt(x, view.Box.Width);
        if (slot < 0)
        {
            _report.Line("QUICKLAUNCH open");
            return;
        }

        if (slot >= dock.Model.Icons.Count)
        {
            ShowQuickLaunch(view, false);
            ShowLauncher(view, true);
            return;
        }

        var icon = dock.Model.Icons[slot];
        ShowQuickLaunch(view, false);
        LaunchEntry(view, icon.Id);
    }

    private bool AdvanceLauncher(LunaView view, long now)
    {
        var animating = false;
        if (view.LauncherUi is { } ui && ui.Fade.Advance(now))
        {
            ui.Frame.Alpha = (float)ui.Fade.Value;
            if (!ui.Fade.IsRunning && !ui.Open)
            {
                ui.Chrome.Enabled = false;
            }

            animating |= ui.Fade.IsRunning;
        }

        if (view.Dock is { Following: false } dock && dock.Slide.Advance(now))
        {
            ApplyDockSlide(dock);
            if (!dock.Slide.IsRunning && !dock.Shown)
            {
                dock.Chrome.Enabled = false;
            }

            animating |= dock.Slide.IsRunning;
        }

        return animating;
    }

    private void SettleLauncher(LunaView view)
    {
        if (view.LauncherUi is { } ui && ui.Fade.IsRunning)
        {
            ui.Fade.Settle();
            ui.Frame.Alpha = (float)ui.Fade.Value;
            ui.Chrome.Enabled = ui.Open;
        }

        if (view.Dock is { } dock && dock.Slide.IsRunning)
        {
            dock.Slide.Settle();
            ApplyDockSlide(dock);
            dock.Chrome.Enabled = dock.Shown;
        }
    }

    private void LaunchEntry(LunaView view, string id)
    {
        if (Desktop.Find(id) is not { } entry)
        {
            _report.Line($"LAUNCH none {id}");
            return;
        }

        var argv = entry.LaunchArgv(ExecLine.TerminalFromEnvironment());
        if (argv is not { Length: > 0 })
        {
            _report.Line($"LAUNCH cannot {id}");
            return;
        }

        ShowLauncher(view, false);
        CloseJustType(view);
        Launch(ExecLine.Join(argv), entry.Name, entry.Icon, entry.Id);
    }

    private void LauncherTabCommand(LunaView view, string name)
    {
        if (view.LauncherUi is not { } ui)
        {
            return;
        }

        ShowLauncher(view, true);
        ui.Model.Select(name);
        _report.Line($"LAUNCHERTAB {ui.Model.Current?.Name ?? "-"} icons={ui.Model.Current?.Icons.Count ?? 0}");
    }

    private void PrintLauncherState(LunaView view)
    {
        if (view.LauncherUi is { } ui)
        {
            _report.Line($"  launcher {(ui.Open ? "open" : "closed")} tab={ui.Model.Current?.Name ?? "-"} reorder={(ui.Model.Reordering ? "on" : "off")}");
            foreach (var tab in ui.Model.Tabs)
            {
                _report.Line($"  tab {tab.Name}: {string.Join(',', tab.Icons.Select(icon => icon.Id))}");
            }
        }

        if (view.Dock is { } dock)
        {
            _report.Line($"  dock {(dock.Shown ? "shown" : "hidden")} {string.Join(',', dock.Model.Icons.Select(icon => icon.Id))}");
        }
    }
}
