using Basin;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Shell.Xdg;
using TabWm.Chrome;

namespace TabWm;

internal sealed partial class TabWm
{
    private const int Cascade = 32;
    private const long LaunchGraceMs = 10_000;

    private readonly List<TabWindow> _windows = [];
    private readonly NestedPopupPlacer _popups = new();
    private readonly Dictionary<Surface, ActivationRedemption> _redeemed = new(ReferenceEqualityComparer.Instance);
    private TabWindow? _focused;
    private Action? _restacked;
    private ulong _nextWindowId;

    internal IReadOnlyList<TabWindow> Windows => _windows;

    internal TabWindow? Focused => _focused;

    internal IUISurface? KeyboardTarget
    {
        get => _seat?.Router.KeyboardFocus;
        set => _seat?.Router.SetKeyboardFocus(value);
    }

    private void AttachWindows()
    {
        Shell.NewToplevel += OnNewToplevel;
        Shell.NewPopup += popup => _popups.Attach(popup);
        _popups.ConstrainBox = toplevel => WindowOf(toplevel)?.Tab?.Set.Box;
        _tokens.Redeemed += OnRedeemed;
        if (_services.Find<XdgActivationManager>() is { } activation)
        {
            activation.ActivationRequested += OnActivationRequested;
        }

        if (_services.Find<XdgToplevelSource>() is { } toplevels)
        {
            toplevels.ActivateRequested += window =>
            {
                if (WindowOf(window) is { } found)
                {
                    Activate(found);
                }
            };
        }
    }

    internal bool MoveRequestedTo(IToplevelHandle toplevel, int x, int y)
    {
        if (WindowOf(toplevel) is not { Mapped: true, Maximized: false, Fullscreen: false } window)
        {
            return false;
        }

        window.MoveTo(x, y);
        Clamp(window);
        _outputs.ScheduleAll();
        return true;
    }

    internal bool ResizeRequestedTo(IToplevelHandle toplevel, int width, int height)
    {
        if (WindowOf(toplevel) is not { Mapped: true, Maximized: false, Fullscreen: false } window)
        {
            return false;
        }

        window.Resize(width, height);
        Clamp(window, (width, height));
        return true;
    }

    internal TabWindow? FindWindow(ulong id)
    {
        foreach (var window in _windows)
        {
            if (window.Id == id)
            {
                return window;
            }
        }

        return null;
    }

    private TabWindow? WindowOf(IToplevelHandle? handle)
    {
        if (handle is null)
        {
            return null;
        }

        foreach (var window in _windows)
        {
            if (ReferenceEquals(window.Handle, handle))
            {
                return window;
            }
        }

        return null;
    }

    internal TabWindow? WindowOwning(Surface surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            foreach (var window in _windows)
            {
                if (ReferenceEquals(candidate, window.Surface))
                {
                    return window;
                }
            }
        }

        return null;
    }

    private void OnNewToplevel(XdgToplevelWindow window)
    {
        var container = new SceneTree(_layers.Hidden);
        var fit = new SceneFit(container);
        var scene = new SceneSurface(fit.Content, window.Surface);
        _popups.Add(window.Xdg, scene);
        var tabWindow = new TabWindow(++_nextWindowId, window, container, scene, fit);
        _windows.Add(tabWindow);

        void Align()
        {
            var geometry = window.Xdg.PresentedGeometry;
            scene.Tree.SetPosition(-geometry.X, -geometry.Y);
            var committed = (geometry.Width, geometry.Height);
            if (tabWindow.Fullscreen && tabWindow.Tab is { } tab && !window.Xdg.HasUnackedConfigure)
            {
                var target = tab.Set.Box.Translated(-tabWindow.X, -tabWindow.Y);
                fit.Update(target, committed, SceneFit.Needed(
                    (target.Width, target.Height), committed, SceneFitState.Fullscreen, SceneFitPolicy.Center));
            }
            else
            {
                fit.Update(new Box(0, 0, geometry.Width, geometry.Height), committed, null);
            }
        }

        Align();
        window.Xdg.Mapped += () => OnMapped(tabWindow);
        window.Xdg.Unmapped += () => OnUnmapped(tabWindow);
        window.Xdg.Committed += () =>
        {
            Align();
            ApplyResizeAnchor(tabWindow);
            tabWindow.UpdateFrame();
        };
        window.TitleChanged += () =>
        {
            if (tabWindow.Frame is { } frame)
            {
                frame.Model.Title = window.Title;
            }

            if (tabWindow.Tab is { } tab)
            {
                RefreshTab(tab);
            }
        };
        window.AppIdChanged += () =>
        {
            if (tabWindow.Tab is { } tab)
            {
                RefreshTab(tab);
            }
        };
        window.Destroyed += () => Forget(tabWindow);
        window.MoveRequested += _ =>
        {
            if (_seat is { } seat)
            {
                BeginMove(tabWindow, seat.PointerX, seat.PointerY);
            }
        };
        window.ResizeRequested += (_, edges) =>
        {
            if (_seat is { } seat)
            {
                BeginResize(tabWindow, edges, seat.PointerX, seat.PointerY);
            }
        };
        window.MaximizeRequested += maximized => SetMaximized(tabWindow, maximized);
        window.FullscreenRequested += fullscreen => SetFullscreen(tabWindow, fullscreen);
        window.MinimizeRequested += () => Minimize(tabWindow);
    }

    private void Forget(TabWindow window)
    {
        if (window.Mapped)
        {
            OnUnmapped(window);
        }

        window.Frame?.Dispose();
        window.Frame = null;
        if (window.Xdg is { } xdg)
        {
            _popups.Remove(xdg.Xdg);
        }

        if (window.Surface is { } surface)
        {
            _redeemed.Remove(surface);
        }

        _windows.Remove(window);
        if (!window.Tree.IsDestroyed)
        {
            window.Tree.Destroy();
        }

        if (ReferenceEquals(_focused, window))
        {
            _focused = null;
        }
    }

    private void OnMapped(TabWindow window)
    {
        if (window.Mapped)
        {
            return;
        }

        var (tab, via) = ChooseTab(window);
        window.Via = via;
        window.Mapped = true;
        Adopt(window, tab);
        if (window.Xdg is { } xdg && _decorationPreferences.IsServerSide(xdg))
        {
            CreateFrame(window);
        }

        PlaceNew(window);
        AnnouncePlaced(window);
    }

    private void AnnouncePlaced(TabWindow window)
    {
        if (window.Tab is not { } tab)
        {
            return;
        }

        BasinReport.Line($"PLACED window={window.Id} tab={tab.Id} via={TabPlacement.Name(window.Via)} app={(window.AppId.Length > 0 ? window.AppId : "-")}");
        if (tab.Shown)
        {
            Focus(window);
        }
        else if (Environment.TickCount64 - tab.LaunchedAtMs > LaunchGraceMs)
        {
            tab.Attention = true;
        }

        RefreshTab(tab);
        UpdatePage(tab.Set);
        UpdateStripVisibility(tab.Set);
        RecomputeAudio();
        MarkSessionDirty();
        _workspaces.MembersChanged();
        _restacked?.Invoke();
        _outputs.ScheduleAll();
    }

    private void Adopt(TabWindow window, Tab tab)
    {
        window.Tab = tab;
        window.Scale = tab.Set.Output.Scale;
        window.Tree.Reparent(window.Fullscreen ? tab.FullscreenTree : tab.Tree);
        window.Tree.RaiseToTop();
        tab.Windows.Insert(0, window);
    }

    private void CreateFrame(TabWindow window)
    {
        var model = new FrameModel(
            () => Minimize(window),
            () => SetMaximized(window, !window.Maximized),
            window.Close)
        {
            Title = window.Title,
            Maximized = window.Maximized,
        };
        window.Frame = new TabFrame(_ui, window.Tree, _index, model);
        window.UpdateFrame();
    }

    private (Tab Tab, PlacementVia Via) ChooseTab(TabWindow window)
    {
        var parent = WindowOf(window.ParentHandle)?.Tab;
        var token = window.Surface is { } surface && _redeemed.TryGetValue(surface, out var redemption) ? TabOfToken(redemption) : null;
        var pid = TabOfPid(window.Pid);
        var set = FocusedSet ?? PrimarySet!;
        var active = set.Active ?? NewTab(set, select: true);
        return TabPlacement.Choose(parent, token, pid, active);
    }

    private Tab? TabOfToken(ActivationRedemption redemption)
    {
        if (redemption.FromLaunch)
        {
            foreach (var set in _sets)
            {
                foreach (var tab in set.Tabs)
                {
                    foreach (var launch in tab.Launches)
                    {
                        if (launch.Token == redemption.Token)
                        {
                            return tab;
                        }
                    }
                }
            }

            return null;
        }

        if (redemption.RequesterPid <= 0)
        {
            return null;
        }

        foreach (var window in _windows)
        {
            if (window.Mapped && window.Pid == redemption.RequesterPid && window.Tab is { } tab)
            {
                return tab;
            }
        }

        return TabOfPid(redemption.RequesterPid);
    }

    private Tab? TabOfPid(int pid)
    {
        if (pid <= 0 || _processes.RootOf(pid) is not { } root)
        {
            return null;
        }

        foreach (var set in _sets)
        {
            foreach (var tab in set.Tabs)
            {
                foreach (var launch in tab.Launches)
                {
                    if (launch.Pid == root.Pid)
                    {
                        return tab;
                    }
                }
            }
        }

        return null;
    }

    private void OnRedeemed(ActivationRedemption redemption)
    {
        _redeemed[redemption.Surface] = redemption;
        if (WindowOwning(redemption.Surface) is not { Mapped: true, Via: PlacementVia.Active, HadInput: false } window ||
            TabOfToken(redemption) is not { } tab || ReferenceEquals(tab, window.Tab))
        {
            return;
        }

        window.Via = PlacementVia.Token;
        MoveWindow(window, tab, follow: false);
        BasinReport.Line($"PLACED window={window.Id} tab={tab.Id} via=token app={(window.AppId.Length > 0 ? window.AppId : "-")}");
    }

    private void OnActivationRequested(Surface surface)
    {
        if (WindowOwning(surface) is { Mapped: true } window)
        {
            Activate(window);
        }
    }

    internal void Activate(TabWindow window)
    {
        if (window.Tab is not { } tab)
        {
            return;
        }

        if (window.Minimized)
        {
            Unminimize(window);
            return;
        }

        if (!tab.Shown)
        {
            SelectTab(tab);
        }

        Focus(window);
    }

    private void PlaceNew(TabWindow window)
    {
        if (window.Tab is not { } tab)
        {
            return;
        }

        if (window.Fullscreen)
        {
            ApplyFullscreen(window);
            return;
        }

        if (window.Maximized)
        {
            ApplyMaximized(window);
            return;
        }

        var area = tab.Set.Work;
        var insets = InsetsOf(window);
        var (width, height) = window.Size;
        var outerWidth = width + insets.Left + insets.Right;
        var outerHeight = height + insets.Top + insets.Bottom;
        var offset = Cascade * (tab.CascadeStep++ % 6);
        var x = area.X + insets.Left + Math.Max(0, (area.Width - outerWidth) / 2) + offset;
        var y = area.Y + insets.Top + Math.Max(0, (area.Height - outerHeight) / 2) + offset;
        window.MoveTo(x, y);
        Clamp(window);
    }

    private static FrameInsets InsetsOf(TabWindow window) =>
        window.Frame is null || window.Fullscreen ? default : TabFrame.Insets;

    private void Clamp(TabWindow window) => Clamp(window, window.Size);

    private void Clamp(TabWindow window, (int Width, int Height) size)
    {
        if (window.Tab is not { } tab || window.Fullscreen || window.Maximized)
        {
            return;
        }

        var area = tab.Set.Work;
        var insets = InsetsOf(window);
        var (width, height) = size;
        var x = window.X;
        var y = window.Y;
        var maxX = area.X + area.Width - width - insets.Right;
        var maxY = area.Y + area.Height - height - insets.Bottom;
        x = Math.Max(area.X + insets.Left, Math.Min(x, Math.Max(area.X + insets.Left, maxX)));
        y = Math.Max(area.Y + insets.Top, Math.Min(y, Math.Max(area.Y + insets.Top, maxY)));
        if (x != window.X || y != window.Y)
        {
            window.MoveTo(x, y);
        }
    }

    private void OnUnmapped(TabWindow window)
    {
        if (!window.Mapped)
        {
            return;
        }

        if (ReferenceEquals(_grab.Window, window))
        {
            EndGrab();
        }

        window.Mapped = false;
        window.Frame?.Dispose();
        window.Frame = null;
        if (window.Curtain is { IsDestroyed: false } curtain)
        {
            curtain.Destroy();
        }

        window.Curtain = null;
        if (!window.Tree.IsDestroyed)
        {
            window.Tree.Reparent(_layers.Hidden);
        }

        var tab = window.Tab;
        window.Tab = null;
        if (tab is null)
        {
            return;
        }

        tab.Windows.Remove(window);
        BasinReport.Line($"UNMAPPED window={window.Id} tab={tab.Id}");
        if (ReferenceEquals(_focused, window))
        {
            _focused = null;
            if (tab.Shown && tab.Focused is { } next)
            {
                Focus(next);
            }
            else
            {
                Seat.Keyboard.NotifyClearFocus();
            }
        }

        if (tab.Closer is { } closer)
        {
            closer.Gone(window);
            if (closer.State == TabCloseState.Done)
            {
                EndClose(tab, removed: tab.IsEmpty);
                if (tab.IsEmpty)
                {
                    return;
                }
            }
            else if (closer.State == TabCloseState.Refused && tab.Set.Banner is not null)
            {
                ShowBanner(tab);
            }
        }

        if (tab.IsEmpty && _config.CloseWhenEmpty && tab.Closer is null && !_stopping)
        {
            RemoveTab(tab);
            return;
        }

        RefreshTab(tab);
        UpdateStripVisibility(tab.Set);
        UpdatePage(tab.Set);
        _workspaces.MembersChanged();
        _outputs.ScheduleAll();
    }

    internal void Focus(TabWindow window)
    {
        if (_layers.IsLocked || window.Tab is not { Shown: true } tab || !window.Mapped)
        {
            return;
        }

        if (_overlay is not null && !ReferenceEquals(_overlay.Set, tab.Set))
        {
            CloseOverlay();
        }

        if (!ReferenceEquals(_focused, window))
        {
            _focused?.SetActivated(false);
        }

        _focused = window;
        tab.Windows.Remove(window);
        tab.Windows.Insert(0, window);
        window.Tree.RaiseToTop();
        window.SetActivated(true);
        if (_overlay is null)
        {
            KeyboardTarget = null;
            if (window.Surface is { } surface)
            {
                Seat.Keyboard.NotifyEnter(surface);
            }
        }

        RefreshTab(tab);
        _restacked?.Invoke();
        _outputs.ScheduleAll();
    }

    private void Unfocus()
    {
        _focused?.SetActivated(false);
        _focused = null;
        Seat.Keyboard.NotifyClearFocus();
    }

    internal void RestoreFocus()
    {
        if (FocusedSet is not { } set)
        {
            return;
        }

        if (set.Active?.Focused is { } window)
        {
            Focus(window);
        }
        else
        {
            UpdatePage(set);
        }
    }

    internal void SetMaximized(TabWindow window, bool maximized)
    {
        if (window.Maximized == maximized)
        {
            return;
        }

        if (window.Frame is { } frame)
        {
            frame.Model.Maximized = maximized;
        }

        if (!window.Mapped)
        {
            window.Maximized = maximized;
            window.SetMaximizedState(maximized);
            return;
        }

        if (maximized)
        {
            if (!window.Fullscreen)
            {
                SaveRestore(window);
            }

            window.Maximized = true;
            window.SetMaximizedState(true);
            if (!window.Fullscreen)
            {
                ApplyMaximized(window);
            }
        }
        else
        {
            window.Maximized = false;
            window.SetMaximizedState(false);
            if (!window.Fullscreen)
            {
                RestoreGeometryOf(window);
            }
        }

        _outputs.ScheduleAll();
    }

    private void ApplyMaximized(TabWindow window)
    {
        if (window.Tab is not { } tab)
        {
            return;
        }

        var area = tab.Set.Work;
        var insets = InsetsOf(window);
        window.Place(new Box(
            area.X + insets.Left,
            area.Y + insets.Top,
            area.Width - insets.Left - insets.Right,
            area.Height - insets.Top - insets.Bottom));
    }

    private static void SaveRestore(TabWindow window)
    {
        window.Restore = window.Restore.Saving(window.Geometry);
    }

    private void RestoreGeometryOf(TabWindow window)
    {
        if (!window.Restore.TryGet(out var saved))
        {
            var area = window.Tab?.Set.Work ?? new Box(0, 0, 800, 600);
            var width = area.Width / 2;
            var height = area.Height / 2;
            saved = new Box(area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2), width, height);
        }

        window.Restore = RestoreGeometry.None;
        window.Place(saved);
        Clamp(window, (saved.Width, saved.Height));
    }

    internal void SetFullscreen(TabWindow window, bool fullscreen)
    {
        if (window.Fullscreen == fullscreen)
        {
            return;
        }

        if (!window.Mapped)
        {
            window.Fullscreen = fullscreen;
            window.SetFullscreenState(fullscreen);
            return;
        }

        if (fullscreen)
        {
            if (!window.Maximized)
            {
                SaveRestore(window);
            }

            window.Fullscreen = true;
            window.SetFullscreenState(true);
            ApplyFullscreen(window);
        }
        else
        {
            window.Fullscreen = false;
            window.SetFullscreenState(false);
            if (window.Curtain is { IsDestroyed: false } curtain)
            {
                curtain.Destroy();
            }

            window.Curtain = null;
            window.Frame?.SetEnabled(true);
            window.X11?.SetFrameExtents(window.Frame is null ? default : TabFrame.Insets);
            if (window.Tab is { } tab)
            {
                window.Tree.Reparent(tab.Tree);
                window.Tree.RaiseToTop();
            }

            if (window.Maximized)
            {
                ApplyMaximized(window);
            }
            else
            {
                RestoreGeometryOf(window);
            }
        }

        if (window.Tab is { } owner)
        {
            UpdateStripVisibility(owner.Set);
        }

        _restacked?.Invoke();
        _outputs.ScheduleAll();
    }

    private void ApplyFullscreen(TabWindow window)
    {
        if (window.Tab is not { } tab)
        {
            return;
        }

        var box = tab.Set.Box;
        window.Frame?.SetEnabled(false);
        window.X11?.SetFrameExtents(default);
        window.Tree.Reparent(tab.FullscreenTree);
        var curtain = window.Curtain is { IsDestroyed: false } existing
            ? existing
            : new SceneRect(tab.FullscreenTree, box.Width, box.Height, new RenderColor(0f, 0f, 0f, 1f));
        curtain.Width = box.Width;
        curtain.Height = box.Height;
        curtain.SetPosition(box.X, box.Y);
        window.Curtain = curtain;
        curtain.RaiseToTop();
        window.Tree.RaiseToTop();
        window.Place(box);
    }

    internal void Minimize(TabWindow window)
    {
        if (!window.Mapped || window.Minimized || window.Tab is not { } tab)
        {
            return;
        }

        window.Minimized = true;
        window.Tree.Enabled = false;
        window.Curtain?.Destroy();
        window.Curtain = null;
        window.X11?.SetMinimized(true);
        if (ReferenceEquals(_focused, window))
        {
            _focused = null;
            window.SetActivated(false);
            if (tab.Focused is { } next)
            {
                Focus(next);
            }
            else
            {
                Seat.Keyboard.NotifyClearFocus();
            }
        }

        RefreshTab(tab);
        UpdateStripVisibility(tab.Set);
        _outputs.ScheduleAll();
    }

    internal void Unminimize(TabWindow window)
    {
        if (!window.Minimized || window.Tab is not { } tab)
        {
            return;
        }

        window.Minimized = false;
        window.Tree.Enabled = true;
        window.X11?.SetMinimized(false);
        if (window.Fullscreen)
        {
            ApplyFullscreen(window);
        }

        SelectTab(tab);
        Focus(window);
        RefreshTab(tab);
        UpdateStripVisibility(tab.Set);
    }

    internal void MoveWindow(TabWindow window, Tab target, bool follow)
    {
        if (window.Tab is not { } source || ReferenceEquals(source, target))
        {
            return;
        }

        var from = source.Set.Work;
        source.Windows.Remove(window);
        if (ReferenceEquals(_focused, window))
        {
            window.SetActivated(false);
            _focused = null;
            if (source.Shown && source.Focused is { } next)
            {
                Focus(next);
            }
            else
            {
                Seat.Keyboard.NotifyClearFocus();
            }
        }

        Adopt(window, target);
        if (window.Fullscreen || window.Maximized)
        {
            Rearranged(window, target.Set);
        }
        else if (!ReferenceEquals(source.Set, target.Set))
        {
            var to = target.Set.Work;
            window.MoveTo(window.X - from.X + to.X, window.Y - from.Y + to.Y);
        }

        Clamp(window);
        window.UpdateFrame();
        BasinReport.Line($"MOVED window={window.Id} tab={target.Id}");
        if (follow)
        {
            SelectTab(target);
            Focus(window);
        }
        else if (!target.Shown)
        {
            window.SetActivated(false);
        }

        RefreshTab(source);
        RefreshTab(target);
        UpdatePage(source.Set);
        UpdatePage(target.Set);
        UpdateStripVisibility(source.Set);
        UpdateStripVisibility(target.Set);
        _workspaces.MembersChanged();
        _outputs.ScheduleAll();
    }

    private static void ApplyResizeAnchor(TabWindow window)
    {
        if (window.ResizeAnchor is not { } anchor)
        {
            return;
        }

        var (width, height) = window.Size;
        var (x, y) = anchor.PositionFor(width, height, window.X, window.Y);
        if (x != window.X || y != window.Y)
        {
            window.MoveTo(x, y);
        }

        window.ResizeAnchor = ResizeAnchor.AfterCommit(window.ResizeAnchor, window.Resizing);
    }
}
