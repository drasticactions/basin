using System.Diagnostics;
using Basin;
using Basin.Host;
using Basin.Backend.Libinput;
using Basin.Cli;
using Basin.Effects;
using Basin.Backend.Wayland;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Capabilities;
using Basin.UI.Skia;
using Wayland;
using Wayland.Server;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private readonly Dictionary<XdgToplevelWindow, ToplevelRestore> _restoring = [];
    private readonly Dictionary<XdgToplevelWindow, Basin.Capabilities.ToplevelSessionState> _saved = [];

    private void WireSessions()
    {
        var sessions = _services.Require<Basin.Desktop.SessionManager>();
        sessions.ToplevelAdded += (session, name, toplevel) =>
        {
            _sessionWindows[toplevel] = (session, name);

            toplevel.Restored += restore => _restoring[toplevel] = restore;
            toplevel.Xdg.Committed += () => SaveSession(toplevel);
            toplevel.Destroyed += () =>
            {
                _sessionWindows.Remove(toplevel);
                _restoring.Remove(toplevel);
                _saved.Remove(toplevel);
            };
        };
    }

    private void SaveSession(XdgToplevelWindow toplevel)
    {
        if (!_sessionWindows.TryGetValue(toplevel, out var key) ||
            _windows.Find(w => w.Toplevel == toplevel) is not { } window)
        {
            return;
        }

        var (width, height) = window.GeometrySize;
        var state = new Basin.Capabilities.ToplevelSessionState
        {
            Geometry = new Box(window.X, window.Y, width, height),
            States = toplevel.SessionStates,
            OutputLayoutId = _layout.Id,
            WorkspaceName = window.Workspace?.Name,
        };

        if (_saved.TryGetValue(toplevel, out var previous) && previous == state)
        {
            return;
        }

        _saved[toplevel] = state;
        _sessionStore.Save(key.Session, key.Name, state);
    }

    private bool RestorePosition(Window window)
    {
        if (!_restoring.Remove(window.Toplevel, out var restore))
        {
            return false;
        }

        if (!restore.State.CanRestorePosition(_layout.Id))
        {
            _report.Line($"RESTORE {restore.Name}: outputs moved, placing fresh");
            return false;
        }

        window.MoveTo(restore.State.Geometry.X, restore.State.Geometry.Y);
        if (restore.State.WorkspaceName is { } workspaceName)
        {
            RestoreWorkspace(window, restore.State.Geometry, workspaceName);
        }

        _report.Line($"RESTORE {restore.Name} at {restore.State.Geometry.X},{restore.State.Geometry.Y}");
        return true;
    }

    private void RestoreWorkspace(Window window, Box geometry, string name)
    {
        var output = _layout.OutputAt(geometry.X + (geometry.Width / 2.0), geometry.Y + (geometry.Height / 2.0));
        var view = Views.FirstOrDefault(v => v.Output == output) ?? ViewAtCursor();
        if (view is null)
        {
            return;
        }

        var target = view.Workspaces.FirstOrDefault(ws => ws.Name == name)
            ?? CreateWorkspace(view, name, afterActive: false);
        if (window.Workspace != target)
        {
            MoveWindowToWorkspace(window, target);
            _report.Line($"RESTORE workspace {name}");
        }
    }

    internal void PlaceCascade(IGrabTarget window)
    {
        var view = Views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? Views[0];
        var origin = _layout.BoxOf(view.Output);
        var usable = FlatArea(view, view.UsableArea.IsEmpty ? origin with { X = 0, Y = 0 } : view.UsableArea);
        var slot = _windows.Count % 8;
        window.MoveTo(origin.X + usable.X + 40 + (slot * 30), origin.Y + usable.Y + 40 + (slot * 30));
    }

    private bool PlaceByRule(Window window)
    {
        if (window.Rule is not { } rule)
        {
            return false;
        }

        var width = rule.Width ?? 0;
        var height = rule.Height ?? 0;
        if (rule.X is null && rule.Y is null && width <= 0 && height <= 0)
        {
            return false;
        }

        var view = Views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? Views[0];
        var origin = _layout.BoxOf(view.Output);
        var flat = FlatArea(view, origin with { X = 0, Y = 0 });
        if (width > 0 && height > 0)
        {
            window.ResizeTo(
                origin.X + flat.X + (rule.X ?? 0), origin.Y + flat.Y + (rule.Y ?? 0), width, height, ResizeEdges.None);
        }
        else
        {
            window.MoveTo(origin.X + flat.X + (rule.X ?? 0), origin.Y + flat.Y + (rule.Y ?? 0));
        }

        return true;
    }

    internal void OnWindowMapped(Window window)
    {
        _windows.Add(window);
        window.Minimized = false;
        _xdgToplevels.SetMinimized(window.Toplevel, false);

        var placed = RestorePosition(window) || PlaceByRule(window);
        if (!placed &&
            !window.Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen) &&
            !window.Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized))
        {
            PlaceCascade(window);
        }

        ApplyCanvas(window);
        PlaceRuleCanvas(window, window.Rule);
        if (window.Workspace is { } mapped && ViewOf(mapped)?.Active != mapped)
        {
            _report.Line($"MAPPED {window.Toplevel.AppId} hidden rule={(window.Rule is null ? "none" : "yes")}");
        }
        else
        {
            FocusWindow(window);
            _report.Line($"MAPPED {window.Toplevel.AppId} rule={(window.Rule is null ? "none" : "yes")}");
        }

        _workspaceModel.RaiseMembersChanged();
    }

    internal void OnWindowGone(Window window)
    {
        _report.Line($"UNMAPPED {window.Toplevel.AppId}");
        _windows.Remove(window);
        ForgetCanvas(window);
        var workspace = window.Workspace;
        window.Workspace = null;
        if (_focused == window)
        {
            if (workspace is not null && ViewOf(workspace)?.Active == workspace)
            {
                FocusWorkspaceWindow(workspace);
            }
            else
            {
                FocusWindow(null);
            }
        }

        if (_grabWindow == window)
        {
            _mode = DragMode.None;
            _effects.OnGrabEnd();
            _grabWindow = null;
        }

        if (workspace is not null && workspace.Tiled.Remove(window))
        {
            DissolveSplit(workspace);
        }

        _workspaceModel.RaiseMembersChanged();
        DropSwitcherCard(window);
        LayoutCanvasAll();
    }

    private void FocusWindow(Window? window)
    {
        if (_focused == window)
        {
            return;
        }

        DismissOpenMenu();

        _focused?.Toplevel.SetActivated(false);
        _focused?.SetDecorationFocus(false);
        _focused = window;
        if (window is not null)
        {
            _focusedX?.SetDecorationFocus(false);
            _focusedX = null;
            if (window.Workspace is { } workspace)
            {
                workspace.LastFocused = window;
            }

            window.Toplevel.SetActivated(true);
            window.SetDecorationFocus(true);
            SlideBackOthers(window, window.Workspace);
            window.Tree?.RaiseToTop();
            _stack.RaiseChanged();
            _seat.Keyboard.NotifyEnter(window.Toplevel.Surface);
            _textInput.NotifyFocus(window.Toplevel.Surface);
        }
        else
        {
            _seat.Keyboard.NotifyClearFocus();
            _textInput.NotifyFocus(null);
        }

        RefreshDim();
    }

    internal void RingBell()
    {
        if (_feedback is null || Views.Count == 0)
        {
            return;
        }

        var box = _focused?.Tree is { IsDestroyed: false }
            ? new Box(_focused.X, _focused.Y, Math.Max(_focused.GeometrySize.Width, 1), Math.Max(_focused.GeometrySize.Height, 1))
            : _layout.BoxOf(Views[0].Output);
        if (_feedback.Bell(box, EffectTick()))
        {
            ScheduleEffectRepaint();
        }
    }

    private FrameTick EffectTick() => new(
        (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency)), 16_666_667);

    private void ScheduleEffectRepaint()
    {
        foreach (var view in Views)
        {
            view.Scheduler?.ScheduleRepaint();
        }
    }

    internal void SetShaded(Window window, bool shaded)
    {
        if (window.Shaded == shaded || window.Tree is null)
        {
            return;
        }

        window.Shaded = shaded;
        window.ApplyShade();
        _report.Line($"SHADED {window.Toplevel.AppId} {(shaded ? "yes" : "no")}");
    }

    internal void SetAbove(Window window, bool above)
    {
        if (window.Above == above || window.Tree is null)
        {
            return;
        }

        window.Above = above;
        window.Tree.Reparent(LayerFor(window));
        window.Tree.RaiseToTop();
        RefreshAboveVisibility();
        window.RefreshFrame();
        _report.Line($"ABOVE {window.Toplevel.AppId} {(above ? "yes" : "no")}");
    }

    internal void SetSticky(Window window, bool sticky)
    {
        if (window.Sticky == sticky || window.Tree is null)
        {
            return;
        }

        window.Sticky = sticky;
        if (!sticky && window.Workspace is { } recorded && ViewOf(recorded) is { Active: { } active })
        {
            window.Workspace = active;
        }

        window.Tree.Reparent(LayerFor(window));
        window.Tree.RaiseToTop();
        window.RefreshFrame();
        _workspaceModel.RaiseMembersChanged();
        _report.Line($"STICKY {window.Toplevel.AppId} {(sticky ? "yes" : "no")}");
    }

    internal SceneTree LayerFor(Window window) =>
        ShelfOwnerOf(window) is { } owner && OverviewOf(owner).ShelfTree is { } shelf ? shelf
        : window.Above ? _layers.Top
        : window.Sticky ? _layers.Windows
        : window.Workspace?.Tree ?? _layers.Windows;

    private void RefreshAboveVisibility()
    {
        foreach (var window in _windows)
        {
            if (window is { Above: true, Tree: { } tree } && !window.Minimized)
            {
                tree.Enabled = window.Sticky || window.Workspace is not { } workspace || ViewOf(workspace)?.Active == workspace;
            }
        }
    }

    private void HideMinimized(Window window)
    {
        if (window.Minimized && window.Tree is { IsDestroyed: false } tree)
        {
            tree.Enabled = false;
        }
    }

    private void SlideBackOthers(IGrabTarget raised, Workspace? workspace)
    {
        if (!_effects.SlideBackEnabled)
        {
            return;
        }

        foreach (var window in _windows)
        {
            if (!ReferenceEquals(window, raised) && window.Workspace == workspace && !window.Minimized)
            {
                _effects.OnRaised(window.Tree, -SlideBackTravel, 0);
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (!ReferenceEquals(xwindow, raised) && xwindow.Workspace == workspace
                && xwindow.Framable && !xwindow.Minimized)
            {
                _effects.OnRaised(xwindow.Tree, -SlideBackTravel, 0);
            }
        }
    }

    private const int SlideBackTravel = 40;

    private const int NotificationTravel = 400;

    internal void SetMinimized(Window window, bool minimized)
    {
        if (window.Minimized == minimized || IsShelved(window))
        {
            return;
        }

        window.Minimized = minimized;
        if (window.Tree is { } tree)
        {
            var (geometryWidth, geometryHeight) = window.GeometrySize;
            var box = new Box(window.X, window.Y, Math.Max(geometryWidth, 1), Math.Max(geometryHeight, 1));
            var (cursorX, cursorY) = ToCanvasPointFor(window, _cursorX, _cursorY);
            if (!minimized)
            {
                tree.Enabled = true;
                _ = _effects.OnMinimize(tree, box, default, restoring: true, cursorX, cursorY);
            }
            else if (!_effects.OnMinimize(
                tree, box, default, restoring: false, cursorX, cursorY,
                () => HideMinimized(window)))
            {
                tree.Enabled = false;
            }
        }

        _xdgToplevels.SetMinimized(window.Toplevel, minimized);
        LayoutCanvasAll();
        if (minimized)
        {
            if (_focused == window)
            {
                if (window.Workspace is { } workspace && ViewOf(workspace)?.Active == workspace)
                {
                    FocusWorkspaceWindow(workspace);
                }
                else
                {
                    FocusWindow(null);
                }
            }
        }
        else
        {
            FocusWindow(window);
        }

        _workspaceModel.RaiseMembersChanged();
    }

    internal void SetMinimized(XWindow xwindow, bool minimized)
    {
        if (xwindow.Minimized == minimized || !xwindow.Framable || IsShelved(xwindow))
        {
            return;
        }

        xwindow.Minimized = minimized;
        xwindow.XWin.SetMinimized(minimized);

        var box = new Box(xwindow.X, xwindow.Y, Math.Max(xwindow.XWin.Width, 1), Math.Max(xwindow.XWin.Height, 1));
        var (xCursorX, xCursorY) = ToCanvasPointFor(xwindow, _cursorX, _cursorY);
        if (!minimized)
        {
            xwindow.Tree.Enabled = true;
            _ = _effects.OnMinimize(xwindow.Tree, box, default, restoring: true, xCursorX, xCursorY);
        }
        else if (!_effects.OnMinimize(
            xwindow.Tree, box, default, restoring: false, xCursorX, xCursorY,
            () => HideMinimized(xwindow)))
        {
            xwindow.Tree.Enabled = false;
        }

        if (minimized)
        {
            if (_focusedX == xwindow)
            {
                _focusedX = null;
                xwindow.SetDecorationFocus(false);
                if (xwindow.Workspace is { } workspace && ViewOf(workspace)?.Active == workspace)
                {
                    FocusWorkspaceWindow(workspace);
                }
            }
        }
        else
        {
            FocusXWindow(xwindow);
        }

        _workspaceModel.RaiseMembersChanged();
        DropSwitcherCard(xwindow);
        _report.Line($"XMINIMIZED {xwindow.XWin.Class} {(minimized ? "on" : "off")}");
    }

    private void HideMinimized(XWindow xwindow)
    {
        if (xwindow.Minimized && !xwindow.Tree.IsDestroyed)
        {
            xwindow.Tree.Enabled = false;
        }
    }

    internal void BeginMove(IGrabTarget window, uint? serial = null)
    {
        if (TraceEnabled)
        {
            Trace($"beginmove win=({window.X},{window.Y}) cursor=({_cursorX:F0},{_cursorY:F0})");
        }

        _mode = DragMode.Move;
        _grabWindow = window;
        var (x, y) = _grabOrigin.For(serial);
        var (canvasX, canvasY) = ToCanvasPointAt(x, y, window);
        _grabX = canvasX - window.X;
        _grabY = canvasY - window.Y;
        BeginCanvasGrab(window, x, y);
        SetCanvasGridDragging(true);
        _effects.OnMoveGrab(
            window.EffectTree,
            _grabX,
            _grabY,
            window switch
            {
                Window { Rule: { } windowRule } => windowRule.WobblyFor(_effects.WobblyEnabled),
                XWindow { Rule: { } xRule } => xRule.WobblyFor(_effects.WobblyEnabled),
                _ => null,
            });
    }

    internal void BeginResize(IGrabTarget window, ResizeEdges edges, uint? serial = null)
    {
        _mode = DragMode.Resize;
        _grabWindow = window;
        _grabEdges = edges;
        var (originX, originY) = _grabOrigin.For(serial);
        (_grabX, _grabY) = ToCanvasPointAt(originX, originY, window);
        var (width, height) = window.GeometrySize;
        _grabStart = new Box(window.X, window.Y, width, height);
        BeginShelfResize(window, edges, originX, originY);
        BeginCanvasResize(window, edges);
        var frame = new Box(0, 0, Math.Max(width, 1), Math.Max(height, 1));
        _effects.OnResizeStart(window.EffectTree, frame, frame, frame, 0, 0);
        window.SetResizing(true);
    }

    private void WirePopup(XdgPopupWindow popup)
    {
        if (popup.Parent is not null && Basin.Desktop.LayerShellSceneDriver.RootLayerOf(popup) is null)
        {
            _popupPlacer.Attach(
                popup,
                _layers.Top,
                origin: () => PopupContentOrigin(popup),
                constrainBox: () => _layout.BoxOf(_layout.OutputAt(_cursorX, _cursorY) ?? Views[0].Output),
                scale: () => PopupScale(popup));
        }

        popup.Xdg.Mapped += () =>
        {
            RefreshSurfaceLuts();
            var origin = ParentOrigin(popup);
            var output = _layout.OutputAt(origin.X + popup.Geometry.X, origin.Y + popup.Geometry.Y)
                ?? Views[0].Output;
            _fractionalScale.AnnounceScale(popup.Surface, output.Scale);
        };
    }

    private double PopupScale(XdgPopupWindow popup)
    {
        var xdg = popup.Parent;
        while (xdg?.Role is XdgPopupWindow parentPopup)
        {
            xdg = parentPopup.Parent;
        }

        return xdg?.Role is XdgToplevelWindow toplevel && FindWindow(toplevel) is { } window
            ? CanvasScaleOf(window)
            : 1.0;
    }

    private Point ParentOrigin(XdgPopupWindow popup)
    {
        var content = PopupContentOrigin(popup);
        var chain = Basin.Desktop.PopupPlacer.ChainOffset(popup);
        return new Point(content.X + chain.X, content.Y + chain.Y);
    }

    private Point PopupContentOrigin(XdgPopupWindow popup)
    {
        var x = 0;
        var y = 0;
        var last = popup;
        var xdg = popup.Parent;
        while (xdg is not null)
        {
            if (xdg.Role is XdgPopupWindow parentPopup)
            {
                last = parentPopup;
                xdg = parentPopup.Parent;
            }
            else
            {
                var geometry = xdg.EffectiveGeometry;
                if (xdg.Role is XdgToplevelWindow toplevel && FindWindow(toplevel) is { } window)
                {
                    var content = window.ScaleBox;
                    if (_canvasStates.TryGetValue(window, out var scaled) && !scaled.Placement.IsIdentity &&
                        ViewOfWindow(window) is { Canvas: { Scales: true } or { Terraces: true } })
                    {
                        var (drawnX, drawnY) = scaled.Placement.Map(content.X, content.Y);
                        x += (int)Math.Round(drawnX);
                        y += (int)Math.Round(drawnY);
                    }
                    else
                    {
                        var screen = MapToScreen(window, content);
                        x += screen.X;
                        y += screen.Y;
                    }
                }
                else
                {
                    x += geometry.X;
                    y += geometry.Y;
                }

                return new Point(x, y);
            }
        }

        if (last.LayerParent is { } layerParent && _layerDriver.SceneOf(layerParent) is { } layerScene)
        {
            x += layerScene.Tree.X;
            y += layerScene.Tree.Y;
        }

        return new Point(x, y);
    }

    private XWindow? FindXWindow(Basin.XWayland.XWaylandWindow xwin)
    {
        foreach (var xwindow in _xwindows)
        {
            if (xwindow.XWin == xwin)
            {
                return xwindow;
            }
        }

        return null;
    }

    private Window? FindWindow(XdgToplevelWindow toplevel)
    {
        foreach (var window in _windows)
        {
            if (window.Toplevel == toplevel)
            {
                return window;
            }
        }

        return null;
    }

    private void ApplyCorners(SceneSurface scene, int radius)
    {
        var shader = CornerShaderFor(radius);
        if (shader is not null || scene.Content.TextureShader is not null)
        {
            scene.Content.TextureShader = shader;
        }
    }

    private Window? FindWindow(Surface surface)
    {
        foreach (var window in _windows)
        {
            if (window.Toplevel.Surface == surface)
            {
                return window;
            }
        }

        return null;
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

        FindWindow(surface)?.SetDecorated(serverSide);
    }

    internal bool IsServerDecorated(XdgToplevelWindow toplevel) =>
        _ssdPreference.TryGetValue(toplevel.Surface, out var serverSide)
            ? serverSide
            : _kdeDecorations.ModeOf(toplevel.Surface) == Basin.Desktop.KdeServerDecorationManager.DecorationMode.Server ||
                _decorations.ModeOf(toplevel) == DecorationMode.ServerSide;

    private void WireLayerShell()
    {
        _layerDriver = new Basin.Desktop.LayerShellSceneDriver(_layerShell, _layout, _layers)
        {
            DefaultOutput = _ => Views.Count > 0
                ? (Views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? Views[0]).Global
                : null,
        };
        _layerDriver.TrackPopups(_shell);
        _layerDriver.PopupSceneCreated += (_, _, _) => RefreshSurfaceLuts();
        _layerDriver.PopupBounds = _ =>
            _layout.BoxOf(_layout.OutputAt(_cursorX, _cursorY) ?? Views[0].Output);
        _layerDriver.SceneCreated += (layer, scene) =>
        {
            RefreshSurfaceLuts();
            if (layer.Namespace.Contains("notification", StringComparison.OrdinalIgnoreCase) && scene is not null)
            {
                _effects.OnNotificationMapped(scene.Tree, NotificationTravel, 0);
            }

            if (layer.KeyboardInteractivity != Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.KeyboardInteractivity.None)
            {
                _seat.Keyboard.NotifyEnter(layer.Surface);
            }
        };
        _layerDriver.Removed += _ =>
        {
            if (_focused is { } focused)
            {
                _seat.Keyboard.NotifyEnter(focused.Toplevel.Surface);
            }
        };
        _layerDriver.UsableAreaChanged += (output, usable) =>
        {
            foreach (var view in Views)
            {
                if (view.Output != output)
                {
                    continue;
                }

                view.UsableArea = usable;
                LayoutCanvas(view);
                foreach (var (layer, _) in _layerDriver.Surfaces)
                {
                    if (layer.Output?.Output == output)
                    {
                        _fractionalScale.AnnounceScale(layer.Surface, view.Output.Scale);
                    }
                }
            }
        };
    }

    private void ArrangeLayerSurfaces() => _layerDriver.Rearrange();

    private void WireSessionLock()
    {
        _lockDriver = new Basin.Desktop.SessionLockSceneDriver(
            _sessionLock, _seat, _layers.Lock, _layout, _layers.SetLocked);
        _lockDriver.Locked += () =>
        {
            CloseOverviewsNow();
            _report.Line($"LOCKED");
        };
        _lockDriver.Unlocked += () =>
        {
            if (_focused is { } focused)
            {
                _seat.Keyboard.NotifyEnter(focused.Toplevel.Surface);
            }

            _report.Line($"UNLOCKED");
        };
        _lockDriver.Abandoned += () => _report.Line($"LOCK ABANDONED (staying blanked)");
        _lockDriver.LockSurfaceAdded += (lockSurface, _) =>
        {
            RefreshSurfaceLuts();
            _fractionalScale.AnnounceScale(lockSurface.Surface, lockSurface.Output.Output.Scale);
        };
    }
}
