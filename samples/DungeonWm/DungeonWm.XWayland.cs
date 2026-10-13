using Basin.Diagnostics;
using Basin.Scene;
using Basin.XWayland;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private readonly XWaylandSceneDriver _xDriver = new();
    private bool _xDriverWired;
    private readonly List<(XWaylandWindow Window, SceneSurface Scene)> _overrides = [];

    private void AttachXWayland(XWaylandWm wm)
    {
        if (_xDriverWired)
        {
            _xDriver.Attach(wm);
            BasinReport.Line($"XWAYLAND WM ready");
            return;
        }

        _xDriverWired = true;
        _xDriver.ManagedParent = _ => _layers.Windows;
        _xDriver.OverrideRedirectParent = _ => _layers.Windows;
        _xDriver.Adopted += OnXAdopted;
        _xDriver.Removed += OnXRemoved;
        _xDriver.ActivationRequested += x =>
        {
            if (WindowOfX(x) is { } window && _focused is not null)
            {
                Focus(window);
            }
        };
        _xDriver.Attach(wm);
        BasinReport.Line($"XWAYLAND WM ready");
    }

    private void OnXAdopted(XWaylandWindow x, SceneSurface scene, bool managed)
    {
        if (!managed)
        {
            _overrides.Add((x, scene));
            ScheduleAll();
            return;
        }

        var window = new DungeonWmWindow(x, scene);
        x.GeometryChanged += () => OnCommitted(window);
        scene.Surface.Committed += () => OnCommitted(window);
        x.FullscreenRequested += fullscreen =>
        {
            if (fullscreen)
            {
                EnterTakeover(window);
            }
            else if (ReferenceEquals(_takeover, window))
            {
                LeaveTakeover();
            }
        };
        x.MaximizeRequested += maximized =>
        {
            if (maximized)
            {
                EnterTakeover(window);
            }
            else if (ReferenceEquals(_takeover, window))
            {
                LeaveTakeover();
            }
        };
        OnMapped(window);
    }

    private void OnXRemoved(XWaylandWindow x, SceneSurface scene)
    {
        _overrides.RemoveAll(entry => ReferenceEquals(entry.Window, x));
        if (WindowOfX(x) is { } window)
        {
            OnUnmapped(window);
        }

        ScheduleAll();
    }

    private DungeonWmWindow? WindowOfX(XWaylandWindow x)
    {
        foreach (var window in _windows)
        {
            if (ReferenceEquals(window.X, x))
            {
                return window;
            }
        }

        return null;
    }

    private DungeonWmWindow? OwnerOf(XWaylandWindow overrideRedirect)
    {
        for (var owner = overrideRedirect.TransientFor; owner is not null; owner = owner.TransientFor)
        {
            if (WindowOfX(owner) is { } window)
            {
                return window;
            }
        }

        return _focused is { X: not null } focused ? focused : null;
    }
}
