using Basin;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Scene;
using Basin.UI.Avalonia;
using TabWm.Chrome;

namespace TabWm;

internal sealed partial class TabWm
{
    private readonly List<TabSet> _sets = [];
    private ulong _nextSetId;

    private TabSet? PrimarySet => _sets.Count > 0 ? _sets[0] : null;

    private void OnOutputAdded(OutputView view)
    {
        var output = view.Output;
        var set = new TabSet(++_nextSetId, output) { Box = _layout.BoxOf(output) };
        set.Usable = UsableOf(set);
        set.Background = new SceneRect(_layers.Background, set.Box.Width, set.Box.Height, new RenderColor(0.16f, 0.17f, 0.19f, 1f));
        set.Background.SetPosition(set.Box.X, set.Box.Y);
        set.StripSurface = new OutputUISurface(_layers.Strip, _ui, _index) { PreciseDamage = true, AccessibleName = "Tab strip" };
        set.StripSurface.Realized += surface =>
        {
            set.StripView = new StripView { DataContext = set.Strip };
            ((AvaloniaUISurface)surface).Content = set.StripView;
        };
        set.Page = new OutputUISurface(_layers.NewTabPages, _ui, _index) { PreciseDamage = true, AutoEnable = false, AccessibleName = "New tab page" };
        set.Page.Realized += surface =>
        {
            ((AvaloniaUISurface)surface).Content = CreatePage(set);
            _host.Loop.AddIdle(() =>
            {
                if (_sets.Contains(set))
                {
                    UpdatePage(set);
                }
            });
        };
        WireStrip(set);
        _sets.Add(set);
        NewTab(set, select: true);

        Arrange();
        _workspaces.Changed();
    }

    private void OnOutputRemoved(OutputView view)
    {
        var set = SetOf(view.Output);
        _usable.Remove(view.Output);
        if (set is null)
        {
            return;
        }

        _sets.Remove(set);
        var target = PrimarySet;
        foreach (var tab in set.Tabs.ToArray())
        {
            if (target is null)
            {
                RemoveTab(tab);
                continue;
            }

            MoveTab(tab, target, target.Tabs.Count, select: false);
        }

        set.StripSurface?.Dispose();
        set.Page?.Dispose();
        set.Banner?.Dispose();
        set.Background?.Destroy();
        Arrange();
        _workspaces.Changed();
    }

    private TabSet? SetOf(IOutput? output)
    {
        foreach (var set in _sets)
        {
            if (ReferenceEquals(set.Output, output))
            {
                return set;
            }
        }

        return null;
    }

    private TabSet? SetAt(double x, double y) => SetOf(_layout.OutputAt(x, y));

    private Box UsableOf(TabSet set) =>
        _usable.TryGetValue(set.Output, out var usable) && !usable.IsEmpty ? set.Box.Intersect(usable) : set.Box;

    private void Arrange()
    {
        foreach (var set in _sets)
        {
            if (!_outputs.Views.Any(view => ReferenceEquals(view.Output, set.Output)))
            {
                continue;
            }

            set.Box = _layout.BoxOf(set.Output);
            set.Usable = UsableOf(set);
            var scale = set.Output.Scale;
            if (set.Background is { } background)
            {
                background.Width = set.Box.Width;
                background.Height = set.Box.Height;
                background.SetPosition(set.Box.X, set.Box.Y);
            }

            set.StripSurface?.PlaceAt(set.StripBox, scale);
            set.Page?.PlaceAt(set.Work, scale);
            set.Banner?.PlaceAt(BannerBox(set), scale);
            UpdateStripVisibility(set);
            UpdatePage(set);
            foreach (var tab in set.Tabs)
            {
                foreach (var window in tab.Windows)
                {
                    Rearranged(window, set);
                }
            }
        }

        _layerDriver.Rearrange();
        _outputs.ScheduleAll();
    }

    private void Rearranged(TabWindow window, TabSet set)
    {
        if (!window.Mapped)
        {
            return;
        }

        var scale = set.Output.Scale;
        if (Math.Abs(scale - window.Scale) > double.Epsilon)
        {
            window.Scale = scale;
            window.UpdateFrame();
        }

        if (window.Fullscreen)
        {
            ApplyFullscreen(window);
        }
        else if (window.Maximized)
        {
            ApplyMaximized(window);
        }
    }

    private void UpdateStripVisibility(TabSet set)
    {
        set.StripHidden = set.Active?.HasFullscreen == true && !set.StripPeek;
        if (set.StripSurface is { } strip)
        {
            strip.Enabled = !set.StripHidden;
        }

        RestackLayers();
    }

    private void RestackLayers()
    {
        _layers.Hidden.RaiseToTop();
        _layers.Background.RaiseToTop();
        _layers.LayerBackground.RaiseToTop();
        _layers.LayerBottom.RaiseToTop();
        _layers.Tabs.RaiseToTop();
        _layers.NewTabPages.RaiseToTop();
        _layers.Strip.RaiseToTop();
        _layers.LayerTop.RaiseToTop();
        _layers.Launcher.RaiseToTop();
        _layers.Fullscreen.RaiseToTop();
        if (_sets.Any(set => set.StripPeek))
        {
            _layers.Strip.RaiseToTop();
        }

        _layers.InputPanel.RaiseToTop();
        _layers.LayerOverlay.RaiseToTop();
        _layers.Popups.RaiseToTop();
        _layers.Cursor.RaiseToTop();
        _layers.Lock.RaiseToTop();
    }

    private void UpdatePage(TabSet set)
    {
        if (set.Page is not { } page)
        {
            return;
        }

        var show = set.Active is { } active && active.IsEmpty && active.Closer is null;
        page.Enabled = show;
        if (show && ReferenceEquals(set, FocusedSet) && _overlay is null && page.Surface is { } surface && !_layers.IsLocked)
        {
            KeyboardTarget = surface;
            Seat.Keyboard.NotifyClearFocus();
            FocusPage(set);
        }
        else if (!show && page.Surface is { } shown && ReferenceEquals(KeyboardTarget, shown))
        {
            KeyboardTarget = null;
        }
    }

    private void WireStrip(TabSet set)
    {
        var strip = set.Strip;
        strip.Selected = model =>
        {
            if (FindTab(model.Id) is { } tab)
            {
                SelectTab(tab);
            }
        };
        strip.CloseRequested = model =>
        {
            if (FindTab(model.Id) is { } tab)
            {
                CloseTab(tab);
            }
        };
        strip.NewTab = () =>
        {
            if (SetOf(set.Output) is { } current)
            {
                NewTab(current, select: true);
            }
        };
        strip.Reordered = (model, index) =>
        {
            if (FindTab(model.Id) is { } tab)
            {
                ReorderTab(tab, index);
            }
        };
        strip.Renamed = (model, label) =>
        {
            if (FindTab(model.Id) is { } tab)
            {
                RenameTab(tab, label);
            }

            if (set.StripSurface?.Surface is { } surface && ReferenceEquals(KeyboardTarget, surface))
            {
                KeyboardTarget = null;
                RestoreFocus();
            }
        };
        strip.RenameStarted = _ =>
        {
            if (set.StripSurface?.Surface is { } surface)
            {
                KeyboardTarget = surface;
                Seat.Keyboard.NotifyClearFocus();
            }
        };
        strip.MuteToggled = model =>
        {
            if (FindTab(model.Id) is { } tab)
            {
                SetMuted(tab, !tab.Muted);
            }
        };
        strip.LaunchInto = model =>
        {
            if (FindTab(model.Id) is { } tab)
            {
                SelectTab(tab);
                ShowOverlay(tab.Set);
            }
        };
        strip.RestoreWindow = id =>
        {
            if (FindWindow(id) is { } window)
            {
                Unminimize(window);
            }
        };
        strip.MenuAction = action => OnMenuAction(action);
        strip.DragOut = model => FindTab(model.Id) is { } tab && BeginTabDrag(tab);
        strip.HeightChanged = height =>
        {
            var rounded = Math.Clamp((int)Math.Ceiling(height), 24, 96);
            if (rounded != set.StripHeight)
            {
                set.StripHeight = rounded;
                Arrange();
            }
        };
    }

    private void OnMenuAction(string action)
    {
        switch (action)
        {
            case "lock":
                Lock();
                break;
            case "logout":
                Stop();
                break;
            case "settings" or "about" when PrimaryOrFocusedSet() is { } set:
                ShowSettings(set, action == "about" ? SettingsAbout : SettingsGeneral);
                break;
        }
    }

    private void DisposeChrome()
    {
        CloseOverlay();
        EndTabDragGhost();
        foreach (var set in _sets)
        {
            set.StripSurface?.Dispose();
            set.Page?.Dispose();
            set.Banner?.Dispose();
        }
    }
}
