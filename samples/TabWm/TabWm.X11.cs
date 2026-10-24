using Basin;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.XWayland;

namespace TabWm;

internal sealed partial class TabWm
{
    private XWaylandSceneDriver? _xdriver;

    private void AttachXWayland()
    {
        if (_xwaylandModule?.Native is not { } native)
        {
            return;
        }

        _xServer = native.Server;
        _xdriver = new XWaylandSceneDriver
        {
            PositionsManagedWindows = false,
            FrameInsets = window => Decorates(window) ? TabFrame.Insets : default,
        };
        _xdriver.ManagedParent = OnX11Managed;
        _xdriver.OverrideRedirectParent = _ => _layers.InputPanel;
        _xdriver.Adopted += OnX11Adopted;
        _xdriver.Removed += (window, _) =>
        {
            if (FindX11(window) is { } tabWindow)
            {
                OnUnmapped(tabWindow);
            }

            _outputs.ScheduleAll();
        };
        _xdriver.ActivationRequested += window =>
        {
            if (FindX11(window) is not { Mapped: true } tabWindow || tabWindow.Tab is not { } tab)
            {
                return;
            }

            if (tab.Shown)
            {
                Focus(tabWindow);
                return;
            }

            tab.Attention = true;
            RefreshTab(tab);
        };
        foreach (var display in _xwaylandModule.Displays)
        {
            display.WindowManagerReady += wm => _xdriver.Attach(wm);
        }

        BasinReport.Line($"XWAYLAND {_xServer.DisplayName}");
    }

    private static bool Decorates(XWaylandWindow window) => window.WantsDecorations && !window.IsFullscreen;

    private TabWindow? FindX11(XWaylandWindow window)
    {
        foreach (var candidate in _windows)
        {
            if (ReferenceEquals(candidate.X11, window))
            {
                return candidate;
            }
        }

        return null;
    }

    private SceneTree? OnX11Managed(XWaylandWindow window)
    {
        if (FindX11(window) is { } known)
        {
            return known.Tree.IsDestroyed ? null : known.Tree;
        }

        var tabWindow = new TabWindow(++_nextWindowId, window, new SceneTree(_layers.Hidden));
        _windows.Add(tabWindow);
        window.GeometryChanged += () =>
        {
            if (tabWindow.X != window.X || tabWindow.Y != window.Y)
            {
                tabWindow.Follow(window.X, window.Y);
            }
            else
            {
                tabWindow.UpdateFrame();
            }

            _outputs.ScheduleAll();
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
        window.PropertiesChanged += () => OnX11Properties(tabWindow);
        window.Destroyed += () => Forget(tabWindow);
        window.MaximizeRequested += maximized => SetMaximized(tabWindow, maximized);
        window.FullscreenRequested += fullscreen => SetFullscreen(tabWindow, fullscreen);
        window.MinimizeRequested += minimize =>
        {
            if (minimize)
            {
                Minimize(tabWindow);
            }
            else
            {
                Unminimize(tabWindow);
            }
        };
        window.MoveRequested += keyboard =>
        {
            if (!keyboard && _seat is { } seat)
            {
                BeginMove(tabWindow, seat.PointerX, seat.PointerY);
            }
        };
        window.ResizeRequested += (edges, keyboard) =>
        {
            if (!keyboard && _seat is { } seat)
            {
                BeginResize(tabWindow, (ResizeEdges)edges, seat.PointerX, seat.PointerY);
            }
        };
        return tabWindow.Tree;
    }

    private void OnX11Adopted(XWaylandWindow window, SceneSurface scene, bool managed)
    {
        RefreshSurfaceLuts();
        _outputs.ScheduleAll();
        if (!managed || FindX11(window) is not { } tabWindow || tabWindow.Mapped)
        {
            return;
        }

        var (tab, via) = ChooseTab(tabWindow);
        tabWindow.Via = via;
        tabWindow.Mapped = true;
        tabWindow.Fullscreen = window.IsFullscreen;
        tabWindow.Maximized = window.IsMaximized;
        Adopt(tabWindow, tab);
        if (Decorates(window))
        {
            CreateFrame(tabWindow);
        }

        PlaceNew(tabWindow);
        AnnouncePlaced(tabWindow);
    }

    private void OnX11Properties(TabWindow window)
    {
        if (window is not { Mapped: true, Via: PlacementVia.Active, HadInput: false } || TabOfPid(window.Pid) is not { } tab ||
            ReferenceEquals(tab, window.Tab))
        {
            return;
        }

        window.Via = PlacementVia.Pid;
        MoveWindow(window, tab, follow: false);
        BasinReport.Line($"PLACED window={window.Id} tab={tab.Id} via=pid app={(window.AppId.Length > 0 ? window.AppId : "-")}");
    }
}
