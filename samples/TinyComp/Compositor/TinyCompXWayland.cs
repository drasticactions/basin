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
    private SceneTree ManagedXParent(Basin.XWayland.XWaylandWindow window)
    {
        if (window.X == 0 && window.Y == 0)
        {
            var slot = (_windows.Count + _xwindows.Count) % 8;
            window.Configure(60 + slot * 30, 60 + slot * 30, window.Width, window.Height);
        }

        _pendingXWorkspace = CurrentWorkspace();
        return new SceneTree(_pendingXWorkspace?.Tree ?? _layers.Windows);
    }

    private Workspace? _pendingXWorkspace;

    private void OnXAdopted(Basin.XWayland.XWaylandWindow window, SceneSurface scene, bool managed)
    {
        var xwindow = new XWindow(this, window, scene, framable: managed);
        if (managed)
        {
            xwindow.Workspace = WorkspaceForRule(xwindow.Rule) ?? _pendingXWorkspace;
            _pendingXWorkspace = null;
            if (xwindow.Workspace is { } assigned && assigned.Tree != xwindow.Tree.Parent)
            {
                xwindow.Tree.Reparent(assigned.Tree);
            }
        }

        _xwindows.Add(xwindow);
        window.GeometryChanged += xwindow.Layout;
        if (managed)
        {
            window.DecorationsChanged += xwindow.UpdateDecorations;
            window.MinimizeRequested += minimized => SetMinimized(xwindow, minimized);
            PlaceXByRule(xwindow);
            _feedback?.OnMapped();
            FocusXWindow(xwindow);
            _effects.OnMapped(xwindow.Tree, xwindow.Rule?.OpenFor(_effects.OpenKind));
        }
    }

    private void PlaceXByRule(XWindow xwindow)
    {
        if (xwindow.Rule is not { } rule)
        {
            return;
        }

        var width = rule.Width ?? 0;
        var height = rule.Height ?? 0;
        if (rule.X is null && rule.Y is null && width <= 0 && height <= 0)
        {
            return;
        }

        var view = Views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? Views[0];
        var origin = _layout.BoxOf(view.Output);
        xwindow.ResizeTo(
            origin.X + (rule.X ?? xwindow.X),
            origin.Y + (rule.Y ?? xwindow.Y),
            width > 0 ? width : xwindow.XWin.Width,
            height > 0 ? height : xwindow.XWin.Height,
            ResizeEdges.None);
    }

    private void OnXRemoved(Basin.XWayland.XWaylandWindow window, SceneSurface scene)
    {
        foreach (var xwindow in _xwindows.ToArray())
        {
            if (ReferenceEquals(xwindow.XWin, window))
            {
                RemoveXWindow(xwindow);
            }
        }
    }

    private void RemoveXWindow(XWindow xwindow)
    {
        if (!_xwindows.Remove(xwindow))
        {
            return;
        }

        if (xwindow.Framable)
        {
            _effects.OnClosing(
                xwindow,
                xwindow.Tree,
                _layers.Top,
                xwindow.DetachCornerRig(),
                xwindow.Rule?.CloseFor(_effects.CloseKind));
        }

        _effects.Forget(xwindow.Tree);
        xwindow.Destroy();
        if (_focusedX == xwindow)
        {
            _focusedX = null;
        }

        if (_grabWindow == xwindow)
        {
            _mode = DragMode.None;
            _effects.OnGrabEnd();
            _grabWindow = null;
        }

        xwindow.Workspace = null;
        _workspaceModel.RaiseMembersChanged();
        DropSwitcherCard(xwindow);
    }

    private void FocusXWindow(XWindow xwindow)
    {
        if (_focused is not null)
        {
            FocusWindow(null);
        }

        if (_focusedX != xwindow)
        {
            DismissOpenMenu();
            _focusedX?.SetDecorationFocus(false);
            _focusedX = xwindow;
            xwindow.SetDecorationFocus(true);
        }

        if (xwindow.Workspace is { } workspace)
        {
            workspace.LastFocused = xwindow;
        }

        xwindow.XWin.Activate();
        xwindow.XWin.Raise();
        SlideBackOthers(xwindow, xwindow.Workspace);
        xwindow.Tree.RaiseToTop();
        _stack.RaiseChanged();
        if (xwindow.XWin.Surface is { } surface)
        {
            _seat.Keyboard.NotifyEnter(surface);
            _textInput.NotifyFocus(surface);
        }

        RefreshDim();
    }

    private void ActivateXWindow(Basin.XWayland.XWaylandWindow window)
    {
        foreach (var xwindow in _xwindows)
        {
            if (xwindow.XWin == window)
            {
                if (xwindow.Workspace is { } workspace && ViewOf(workspace) is { } view && view.Active != workspace)
                {
                    MarkUrgent(workspace);
                }
                else
                {
                    SetMinimized(xwindow, false);
                    FocusXWindow(xwindow);
                }

                return;
            }
        }
    }
}
