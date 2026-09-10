using Basin;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Shell.Xdg;
using MauiComp.Shell;
using Xkb;

namespace MauiComp;

internal sealed partial class MauiComp
{
    private static readonly XkbKeysym Tab = XkbKeysym.FromName("Tab");
    private static readonly XkbKeysym ShiftTab = XkbKeysym.FromName("ISO_Left_Tab");
    private static readonly XkbKeysym F4 = XkbKeysym.FromName("F4");
    private static readonly XkbKeysym Escape = XkbKeysym.FromName("Escape");
    private static readonly XkbKeysym AltLeft = XkbKeysym.FromName("Alt_L");
    private static readonly XkbKeysym AltRight = XkbKeysym.FromName("Alt_R");

    private readonly List<ShellWindow> _windows = [];
    private readonly Dictionary<XdgSurfaceState, SceneSurface> _surfaceScenes = [];
    private readonly List<PanelModel> _panels = [];
    private readonly Dictionary<Surface, bool> _ssdPreference = [];
    private ShellWindow? _focused;
    private ShellSwitcher? _switcher;
    private ShellStartMenu? _startMenu;
    private ShellRunDialog? _runDialog;
    private List<AppEntry>? _programs;

    internal IReadOnlyList<ShellWindow> Windows => _windows;

    private void WireWindows()
    {
        Shell.NewToplevel += OnNewToplevel;
        Shell.NewPopup += OnNewPopup;
        _switcher = new ShellSwitcher(_ui, _mauiSurfaces, _layers.Overlay, _shellSurfaces)
        {
            Area = PrimaryBox,
            Scale = () => _outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0,
            Changed = _outputs.ScheduleAll,
            Chosen = Focus,
        };

        _startMenu = new ShellStartMenu(_ui, _mauiSurfaces, _layers.Overlay, _shellSurfaces, OpenRun, Stop)
        {
            Area = PrimaryBox,
            Scale = () => _outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0,
            Changed = _outputs.ScheduleAll,
            Launch = Spawn,
            Programs = () => _programs ??= DesktopEntries.Scan(),
        };
        _runDialog = new ShellRunDialog(_ui, _mauiSurfaces, _layers.Overlay, _shellSurfaces, Spawn)
        {
            Area = PrimaryBox,
            Scale = () => _outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0,
            Changed = _outputs.ScheduleAll,
            FocusChanged = FocusChrome,
        };
    }

    private void DisposeWindows()
    {
        _runDialog?.Dispose();
        _startMenu?.Dispose();
        foreach (var window in _windows)
        {
            window.Titlebar?.Dispose();
            window.Titlebar = null;
        }
    }

    private void WirePanel(PanelModel panel)
    {
        _panels.Add(panel);
        panel.StartRequested = () => _startMenu?.Toggle();
        foreach (var window in _windows)
        {
            panel.Tasks.Add(new TaskEntry(window.Label, () => Focus(window)) { Active = ReferenceEquals(window, _focused) });
        }
    }

    private void OpenRun()
    {
        _startMenu?.Close();
        _runDialog?.Open();
    }

    private void FocusChrome(Basin.Capabilities.IUISurface? surface)
    {
        if (_seat is null)
        {
            return;
        }

        if (surface is not null)
        {
            Seat.Keyboard.NotifyClearFocus();
            _seat.Router.SetKeyboardFocus(surface);
            return;
        }

        _seat.Router.SetKeyboardFocus(null);
        if (_focused is { } window && window.Window.IsMapped)
        {
            Seat.Keyboard.NotifyEnter(window.Window.Surface);
        }
    }

    private void DriveLauncher(string? action)
    {
        switch (action)
        {
            case null:
                _startMenu?.Open();
                break;
            case "close":
                _startMenu?.Close();
                break;
            case "programs":
                _startMenu?.Open();
                _startMenu?.ShowPrograms();
                break;
            case "run":
                OpenRun();
                break;
        }
    }

    private bool OnChromeClick(Basin.Capabilities.IUISurface? hovered)
    {
        if (_startMenu is { IsOpen: true } menu && !menu.OwnsSurface(hovered) &&
            !_uiDriver.Popups.Any(p => ReferenceEquals(p.Surface, hovered)) &&
            !(_seat is not null && _shell.IsStartButtonAt(hovered, _seat.PointerX, _seat.PointerY)))
        {
            menu.Close();
        }

        if (_seat is null)
        {
            return false;
        }

        if (FrameEdgeAt(_seat.PointerX, _seat.PointerY, out var framed) is var edges && edges != ResizeEdges.None && framed is not null)
        {
            Focus(framed);
            BeginInteractive(framed, CursorMode.Resize, edges);
            return true;
        }

        if (hovered is null)
        {
            return false;
        }

        if (_runDialog is { } dialog && dialog.OwnsSurface(hovered))
        {
            if (dialog.IsDragHandleAt(_seat.PointerX, _seat.PointerY))
            {
                BeginDialogDrag(dialog);
                return true;
            }

            return false;
        }

        foreach (var window in _windows)
        {
            if (window.Titlebar is { } titlebar && titlebar.OwnsSurface(hovered))
            {
                Focus(window);
                if (!titlebar.IsDragHandleAt(_seat.PointerX, _seat.PointerY))
                {
                    _lastCaptionPress = null;
                    return false;
                }

                var now = Environment.TickCount64;
                if (_lastCaptionPress is { } last && ReferenceEquals(last.Window, window) &&
                    now - last.AtMs <= DoubleClickMs)
                {
                    _lastCaptionPress = null;
                    SetMaximized(window, !window.Maximized);
                    return true;
                }

                _lastCaptionPress = (window, now);
                if (!window.Maximized)
                {
                    BeginInteractive(window, CursorMode.Move, ResizeEdges.None);
                }

                return true;
            }
        }

        _lastCaptionPress = null;
        return false;
    }

    private const long DoubleClickMs = 400;
    private (ShellWindow Window, long AtMs)? _lastCaptionPress;

    private void DriveSwitcher(string action)
    {
        switch (action)
        {
            case "open":
                _switcher?.Open(_windows.Where(w => w.Window.IsMapped && !w.Minimized));
                break;
            case "next":
                _switcher?.Next();
                break;
            case "commit":
                _switcher?.Commit();
                break;
            case "cancel":
                _switcher?.Cancel();
                break;
        }
    }

    private void RescaleTitlebars()
    {
        var scale = _outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0;
        foreach (var window in _windows)
        {
            window.Titlebar?.Update(scale);
        }
    }

    private void OnNewToplevel(XdgToplevelWindow toplevel)
    {
        var scene = new SceneSurface(_layers.Windows, toplevel.Surface);
        scene.Tree.Enabled = false;
        _surfaceScenes[toplevel.Xdg] = scene;
        var window = new ShellWindow(toplevel, scene);

        toplevel.Xdg.Mapped += () => OnMapped(window);
        toplevel.Xdg.Unmapped += () => OnUnmapped(window);
        toplevel.Xdg.Committed += () => OnCommitted(window);
        toplevel.Destroyed += () =>
        {
            _surfaceScenes.Remove(toplevel.Xdg);
            OnUnmapped(window);
        };
        toplevel.TitleChanged += () =>
        {
            window.Titlebar?.SetTitle(window.Label);
            if (window.Task is { } task)
            {
                task.Title = window.Label;
            }
        };
        toplevel.MoveRequested += _ => BeginInteractive(window, CursorMode.Move, ResizeEdges.None);
        toplevel.ResizeRequested += (_, edges) => BeginInteractive(window, CursorMode.Resize, edges);
        toplevel.MaximizeRequested += maximize => SetMaximized(window, maximize);
        toplevel.FullscreenRequested += fullscreen => SetFullscreen(window, fullscreen);
        toplevel.MinimizeRequested += () => Minimize(window);
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (popup.Parent is not { } parent || !_surfaceScenes.TryGetValue(parent, out var parentScene))
        {
            return;
        }

        var scene = new SceneSurface(parentScene.Tree, popup.Surface);
        _surfaceScenes[popup.Xdg] = scene;

        void Place()
        {
            var origin = parent.EffectiveGeometry;
            var offset = popup.SurfacePosition;
            scene.Tree.SetPosition(origin.X + offset.X, origin.Y + offset.Y);
        }

        Place();
        popup.Xdg.Committed += Place;
        popup.GeometryChanged += Place;
        popup.Destroyed += () =>
        {
            popup.Xdg.Committed -= Place;
            popup.GeometryChanged -= Place;
            _surfaceScenes.Remove(popup.Xdg);
        };
    }

    private void OnMapped(ShellWindow window)
    {
        if (_windows.Contains(window))
        {
            return;
        }

        var work = WorkArea();
        var geometry = window.Window.Xdg.EffectiveGeometry;
        var x = work.X + Math.Max(0, (work.Width - geometry.Width) / 2) - geometry.X;
        var y = work.Y + ShellTitlebar.Height + Math.Max(0, (work.Height - ShellTitlebar.Height - geometry.Height) / 2) - geometry.Y;
        window.Tree.SetPosition(x, y);
        window.Tree.Enabled = true;
        _windows.Insert(0, window);

        if (IsServerDecorated(window.Window))
        {
            AddTitlebar(window);
        }

        window.Task = new TaskEntry(window.Label, () => Focus(window));
        foreach (var panel in _panels)
        {
            panel.Tasks.Add(window.Task);
        }

        Focus(window);
        _animations.BeginMap(window);
        _outputs.ScheduleAll();
    }

    private void AddTitlebar(ShellWindow window)
    {
        if (window.Titlebar is not null)
        {
            return;
        }

        window.Titlebar = new ShellTitlebar(
            _ui,
            _mauiSurfaces,
            _shellSurfaces,
            window,
            () => window.Window.Close(),
            () => SetMaximized(window, !window.Maximized),
            () => Minimize(window));
        window.Titlebar.Update(_outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0);
        window.Titlebar.SetActive(ReferenceEquals(window, _focused));
        window.Titlebar.Visible = !window.Fullscreen;
    }

    private void RecordDecorationPreference(Surface surface, bool serverSide)
    {
        if (_ssdPreference.TryAdd(surface, serverSide))
        {
            surface.Destroyed += () => _ssdPreference.Remove(surface);
        }
        else
        {
            _ssdPreference[surface] = serverSide;
        }

        var window = _windows.FirstOrDefault(w => ReferenceEquals(w.Window.Surface, surface));
        if (window is null)
        {
            return;
        }

        if (serverSide)
        {
            AddTitlebar(window);
        }
        else
        {
            window.Titlebar?.Dispose();
            window.Titlebar = null;
        }

        _outputs.ScheduleAll();
    }

    private bool IsServerDecorated(XdgToplevelWindow toplevel) =>
        _ssdPreference.TryGetValue(toplevel.Surface, out var serverSide)
            ? serverSide
            : KdeDecorations.ModeOf(toplevel.Surface) == KdeServerDecorationManager.DecorationMode.Server ||
                Decorations.ModeOf(toplevel) == DecorationMode.ServerSide;

    private void OnUnmapped(ShellWindow window)
    {
        if (!_windows.Remove(window))
        {
            return;
        }

        _animations.BeginUnmap(window);
        window.Curtain?.Destroy();
        window.Curtain = null;
        if (ReferenceEquals(_grabbed, window))
        {
            ResetCursorMode();
        }

        _switcher?.Forget(window);
        window.Tree.Enabled = false;
        window.Titlebar?.Dispose();
        window.Titlebar = null;
        if (window.Task is { } task)
        {
            foreach (var panel in _panels)
            {
                panel.Tasks.Remove(task);
            }

            window.Task = null;
        }

        if (ReferenceEquals(_focused, window))
        {
            _focused = null;
            Focus(_windows.FirstOrDefault(w => w.Window.IsMapped && !w.Minimized));
        }

        _outputs.ScheduleAll();
    }

    private void OnCommitted(ShellWindow window)
    {
        if (!window.Window.IsMapped)
        {
            return;
        }

        if (window.Fullscreen)
        {
            PlaceFullscreen(window);
        }
        else if (window.Maximized)
        {
            PlaceMaximized(window);
        }
        else if (window.RestorePending)
        {
            PlaceRestored(window);
        }

        if (window.Titlebar is { } titlebar)
        {
            titlebar.Framed = !window.Maximized;
            titlebar.Update(_outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0);
            titlebar.Visible = !window.Fullscreen;
        }
    }

    private ResizeEdges FrameEdgeAt(double x, double y, out ShellWindow? hit)
    {
        foreach (var window in _windows)
        {
            if (window.Titlebar is { } titlebar && !window.Minimized && !window.Maximized && !window.Fullscreen &&
                titlebar.EdgeAt(x, y) is var edges && edges != ResizeEdges.None)
            {
                hit = window;
                return edges;
            }
        }

        hit = null;
        return ResizeEdges.None;
    }

    private string? ChromeCursorAt(double x, double y) =>
        IsGrabbing ? null : ShellTitlebar.CursorFor(FrameEdgeAt(x, y, out _));

    private void SetFullscreen(ShellWindow window, bool fullscreen)
    {
        if (window.Fullscreen == fullscreen)
        {
            return;
        }

        if (fullscreen)
        {
            if (!window.Maximized)
            {
                window.RestorePending = false;
                window.Restore = window.Geometry;
            }

            window.Fullscreen = true;
            window.Window.SetFullscreen(true);
            var box = PrimaryBox();
            window.Window.SetSize(box.Width, box.Height);
            window.Tree.Reparent(_layers.Fullscreen);
            window.Curtain ??= new SceneRect(_layers.Fullscreen, box.Width, box.Height, new RenderColor(0f, 0f, 0f, 1f));
            window.Curtain.Width = box.Width;
            window.Curtain.Height = box.Height;
            window.Curtain.SetPosition(box.X, box.Y);
            window.Curtain.LowerToBottom();
            PlaceFullscreen(window);
            if (window.Titlebar is { } titlebar)
            {
                titlebar.Visible = false;
            }
        }
        else
        {
            window.Fullscreen = false;
            window.Window.SetFullscreen(false);
            window.Curtain?.Destroy();
            window.Curtain = null;
            window.Tree.Reparent(_layers.Windows);
            if (window.Maximized)
            {
                var work = WorkArea();
                window.Window.SetSize(work.Width, work.Height - ShellTitlebar.Height);
                PlaceMaximized(window);
            }
            else
            {
                window.Window.SetSize(window.Restore.Width, window.Restore.Height);
                window.RestorePending = true;
                PlaceRestored(window);
            }

            if (window.Titlebar is { } titlebar)
            {
                titlebar.Visible = true;
                titlebar.Update(_outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0);
            }

            if (ReferenceEquals(window, _focused))
            {
                window.Tree.RaiseToTop();
            }
        }

        _outputs.ScheduleAll();
    }

    private void PlaceFullscreen(ShellWindow window)
    {
        var box = PrimaryBox();
        var geometry = window.Window.Xdg.EffectiveGeometry;
        var x = box.X + Math.Max(0, (box.Width - geometry.Width) / 2) - geometry.X;
        var y = box.Y + Math.Max(0, (box.Height - geometry.Height) / 2) - geometry.Y;
        window.Tree.SetPosition(x, y);
    }

    private void PlaceRestored(ShellWindow window)
    {
        var geometry = window.Window.Xdg.EffectiveGeometry;
        window.Tree.SetPosition(window.Restore.X - geometry.X, window.Restore.Y - geometry.Y);
        if (geometry.Width == window.Restore.Width && geometry.Height == window.Restore.Height)
        {
            window.RestorePending = false;
        }
    }

    private void PlaceMaximized(ShellWindow window)
    {
        var work = WorkArea();
        var geometry = window.Window.Xdg.EffectiveGeometry;
        var top = work.Y + (window.Titlebar is null ? 0 : ShellTitlebar.Height);
        window.Tree.SetPosition(work.X - geometry.X, top - geometry.Y);
    }

    internal void Focus(ShellWindow? window)
    {
        if (window is null)
        {
            if (_focused is { } previous)
            {
                previous.Window.SetActivated(false);
                previous.Titlebar?.SetActive(false);
                if (previous.Task is { } task)
                {
                    task.Active = false;
                }
            }

            _focused = null;
            Seat.Keyboard.NotifyEnter(null);
            return;
        }

        if (window.Minimized)
        {
            window.Minimized = false;
            window.Tree.Enabled = true;
            _animations.BeginRestore(window, IconBoxOf(window));
        }

        if (_focused is { } old && !ReferenceEquals(old, window))
        {
            old.Window.SetActivated(false);
            old.Titlebar?.SetActive(false);
            if (old.Task is { } oldTask)
            {
                oldTask.Active = false;
            }
        }

        _focused = window;
        window.Tree.RaiseToTop();
        _windows.Remove(window);
        _windows.Insert(0, window);
        window.Window.SetActivated(true);
        window.Titlebar?.SetActive(true);
        if (window.Task is { } active)
        {
            active.Active = true;
        }

        Seat.Keyboard.NotifyEnter(window.Window.Surface);
        _outputs.ScheduleAll();
    }

    private void SetMaximized(ShellWindow window, bool maximized)
    {
        if (maximized && !window.Maximized)
        {
            window.RestorePending = false;
            window.Restore = window.Geometry;
            var work = WorkArea();
            var titlebar = window.Titlebar is null ? 0 : ShellTitlebar.Height;
            window.Window.SetSize(work.Width, work.Height - titlebar);
            window.Maximized = true;
            PlaceMaximized(window);
        }
        else if (!maximized && window.Maximized)
        {
            window.Window.SetSize(window.Restore.Width, window.Restore.Height);
            window.RestorePending = true;
            PlaceRestored(window);
        }

        window.Maximized = maximized;
        window.Window.SetMaximized(maximized);
        _outputs.ScheduleAll();
    }

    private void Minimize(ShellWindow window)
    {
        if (window.Minimized)
        {
            return;
        }

        window.Minimized = true;
        if (!_animations.BeginMinimize(window, IconBoxOf(window), () => HideMinimized(window)))
        {
            HideMinimized(window);
        }

        if (ReferenceEquals(_focused, window))
        {
            _focused = null;
            window.Window.SetActivated(false);
            window.Titlebar?.SetActive(false);
            if (window.Task is { } task)
            {
                task.Active = false;
            }

            Focus(_windows.FirstOrDefault(w => !w.Minimized && w.Window.IsMapped));
        }

        _outputs.ScheduleAll();
    }

    private void HideMinimized(ShellWindow window)
    {
        if (window.Minimized && !window.Tree.IsDestroyed)
        {
            window.Tree.Enabled = false;
            _outputs.ScheduleAll();
        }
    }

    private Box IconBoxOf(ShellWindow window)
    {
        if (window.Task is { } task && _shell.TaskIconBox(task) is { IsEmpty: false } box)
        {
            return box;
        }

        var work = WorkArea();
        return new Box(work.X + 8, work.Bottom, 150, ShellOutputs.PanelThickness);
    }

    private bool OnKeyBinding(uint time, uint key, bool pressed)
    {
        var symbol = Seat.Keyboard.KeysymFor(key);
        var alt = Seat.Keyboard.State?.IsModActive("Mod1") == true;

        if (pressed && symbol == Escape && _startMenu is { IsOpen: true })
        {
            _startMenu.Close();
            return true;
        }

        if (pressed && symbol == Escape && _runDialog is { IsOpen: true })
        {
            _runDialog.Close();
            return true;
        }

        if (_switcher is { IsOpen: true })
        {
            if (pressed && symbol == Escape)
            {
                _switcher.Cancel();
                return true;
            }

            if (pressed && (symbol == Tab || symbol == ShiftTab))
            {
                if (Seat.Keyboard.State?.IsModActive("Shift") == true)
                {
                    _switcher.Previous();
                }
                else
                {
                    _switcher.Next();
                }

                return true;
            }

            if (!pressed && (symbol == AltLeft || symbol == AltRight))
            {
                _switcher.Commit();
                return false;
            }

            return true;
        }

        if (pressed && alt && (symbol == Tab || symbol == ShiftTab))
        {
            _switcher?.Open(_windows.Where(w => w.Window.IsMapped && !w.Minimized));
            return true;
        }

        if (pressed && alt && symbol == F4)
        {
            _focused?.Window.Close();
            return true;
        }

        return false;
    }
}
