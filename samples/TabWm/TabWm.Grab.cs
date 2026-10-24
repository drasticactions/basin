using Avalonia.Controls;
using Avalonia.Layout;
using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;
using TabWm.Chrome;

namespace TabWm;

internal sealed partial class TabWm
{
    private const int HoverSwitchMs = 600;

    private readonly TabGrab _grab = new();
    private IEventSource? _hoverTimer;
    private AvaloniaUISurface? _ghost;
    private UISurfaceNode? _ghostNode;

    internal TabGrab Grab => _grab;

    internal void BeginMove(TabWindow window, double pointerX, double pointerY)
    {
        if (window.Maximized || window.Fullscreen || !window.Mapped)
        {
            return;
        }

        _grab.Kind = TabGrabKind.Move;
        _grab.Window = window;
        _grab.StartX = pointerX;
        _grab.StartY = pointerY;
        _grab.OriginX = window.X;
        _grab.OriginY = window.Y;
        _grab.Hover = null;
        _cursor.ShowNamed("grabbing");
    }

    internal void BeginResize(TabWindow window, ResizeEdges edges, double pointerX, double pointerY)
    {
        if (window.Maximized || window.Fullscreen || !window.Mapped)
        {
            return;
        }

        var (width, height) = window.Size;
        _grab.Kind = TabGrabKind.Resize;
        _grab.Window = window;
        _grab.Edges = edges == ResizeEdges.None ? ResizeEdges.BottomRight : edges;
        _grab.StartX = pointerX;
        _grab.StartY = pointerY;
        _grab.OriginX = window.X;
        _grab.OriginY = window.Y;
        _grab.OriginWidth = width;
        _grab.OriginHeight = height;
        window.ResizeAnchor = new ResizeAnchor(_grab.Edges, window.X + width, window.Y + height);
        window.Resizing = true;
        window.Xdg?.SetResizing(true);
    }

    internal bool BeginTabDrag(Tab tab)
    {
        if (_grab.Active || _seat is null)
        {
            return false;
        }

        _grab.Kind = TabGrabKind.TabDrag;
        _grab.Tab = tab;
        _grab.StartX = _seat.PointerX;
        _grab.StartY = _seat.PointerY;
        ShowGhost(tab.Label, _seat.PointerX, _seat.PointerY, tab.Set.Output.Scale);
        BasinReport.Line($"TABDRAG tab={tab.Id}");
        return true;
    }

    private void OnPointerMoved(double x, double y)
    {
        if (!_grab.Active)
        {
            return;
        }

        var dx = x - _grab.StartX;
        var dy = y - _grab.StartY;
        switch (_grab.Kind)
        {
            case TabGrabKind.Move when _grab.Window is { } window:
                window.MoveTo(_grab.OriginX + (int)dx, _grab.OriginY + (int)dy);
                HoverHeader(x, y);
                _outputs.ScheduleAll();
                break;
            case TabGrabKind.Resize when _grab.Window is { } resized:
                Resize(resized, dx, dy);
                break;
            case TabGrabKind.TabDrag:
                MoveGhost(x, y);
                break;
        }
    }

    private void Resize(TabWindow window, double dx, double dy)
    {
        var width = _grab.OriginWidth;
        var height = _grab.OriginHeight;
        if (_grab.Edges.HasFlag(ResizeEdges.Right))
        {
            width = Math.Max(1, _grab.OriginWidth + (int)dx);
        }
        else if (_grab.Edges.HasFlag(ResizeEdges.Left))
        {
            width = Math.Max(1, _grab.OriginWidth - (int)dx);
        }

        if (_grab.Edges.HasFlag(ResizeEdges.Bottom))
        {
            height = Math.Max(1, _grab.OriginHeight + (int)dy);
        }
        else if (_grab.Edges.HasFlag(ResizeEdges.Top))
        {
            height = Math.Max(1, _grab.OriginHeight - (int)dy);
        }

        if (window.X11 is { } x11)
        {
            var x = _grab.Edges.HasFlag(ResizeEdges.Left) ? _grab.OriginX + _grab.OriginWidth - width : window.X;
            var y = _grab.Edges.HasFlag(ResizeEdges.Top) ? _grab.OriginY + _grab.OriginHeight - height : window.Y;
            window.Follow(x, y);
            x11.Configure(x, y, width, height);
            return;
        }

        window.Resize(width, height);
    }

    private void HoverHeader(double x, double y)
    {
        var hover = TabUnder(x, y);
        if (ReferenceEquals(hover, _grab.Hover))
        {
            return;
        }

        if (_grab.Hover is { } previous)
        {
            previous.Model.DropTarget = false;
        }

        _grab.Hover = hover;
        _hoverTimer?.Remove();
        _hoverTimer = null;
        if (hover is null || ReferenceEquals(hover, _grab.Window?.Tab))
        {
            return;
        }

        hover.Model.DropTarget = true;
        _hoverTimer = _host.Loop.AddTimer(() =>
        {
            _hoverTimer?.Remove();
            _hoverTimer = null;
            if (_grab is { Kind: TabGrabKind.Move, Window: { } dragged, Hover: { } target } && !ReferenceEquals(dragged.Tab, target))
            {
                target.Model.DropTarget = false;
                MoveWindow(dragged, target, follow: true);
                _grab.Hover = null;
            }
        });
        _hoverTimer.UpdateTimer(HoverSwitchMs);
    }

    private Tab? TabUnder(double x, double y)
    {
        if (SetAt(x, y) is not { } set || set.StripHidden)
        {
            return null;
        }

        var strip = set.StripBox;
        if (x < strip.X || x >= strip.X + strip.Width || y < strip.Y || y >= strip.Y + strip.Height || set.StripView is not { } view)
        {
            return null;
        }

        var index = view.IndexAtStrip(x - strip.X);
        return index >= 0 && index < set.Tabs.Count ? set.Tabs[index] : null;
    }

    private void EndGrab(uint time = 0, uint button = InputCodes.BtnLeft)
    {
        _hoverTimer?.Remove();
        _hoverTimer = null;
        switch (_grab.Kind)
        {
            case TabGrabKind.Resize when _grab.Window is { } window:
                window.Resizing = false;
                window.Xdg?.SetResizing(false);
                window.Xdg?.RequestConfigure();
                break;
            case TabGrabKind.Move when _grab.Window is { } moved:
                if (_grab.Hover is { } target && !ReferenceEquals(target, moved.Tab) && _seat is { } seat && ReferenceEquals(TabUnder(seat.PointerX, seat.PointerY), target))
                {
                    target.Model.DropTarget = false;
                    moved.MoveTo(_grab.OriginX, _grab.OriginY);
                    MoveWindow(moved, target, follow: false);
                }
                else if (_grab.Hover is { } hovered)
                {
                    hovered.Model.DropTarget = false;
                }

                if (moved.Mapped)
                {
                    Clamp(moved);
                }

                break;
            case TabGrabKind.TabDrag when _grab.Tab is { } tab:
                DropTab(tab, time, button);
                break;
        }

        _grab.Clear();
        _cursor.ShowNamed("default");
        _seat?.RefreshPointer();
    }

    private void DropTab(Tab tab, uint time, uint button)
    {
        EndTabDragGhost();
        _seat?.ReleaseChrome(time, button);
        if (_seat is not { } seat || SetAt(seat.PointerX, seat.PointerY) is not { } set)
        {
            BasinReport.Line($"TABDROP tab={tab.Id} cancelled");
            return;
        }

        var strip = set.StripBox;
        var inside = seat.PointerY >= strip.Y - StripView.DragOutDistance && seat.PointerY < strip.Y + strip.Height + StripView.DragOutDistance;
        if (!inside || FindTab(tab.Id) is null)
        {
            BasinReport.Line($"TABDROP tab={tab.Id} cancelled");
            return;
        }

        var index = set.StripView?.IndexAtStrip(seat.PointerX - strip.X) ?? -1;
        MoveTab(tab, set, index < 0 ? set.Tabs.Count : index, select: true);
        BasinReport.Line($"TABDROP tab={tab.Id} output={set.Output.Name}");
    }

    private void ShowGhost(string label, double x, double y, double scale)
    {
        EndTabDragGhost();
        _ghost = _ui.CreateSurface(new UISurfaceOptions { Target = _ui.Produces, Width = 200, Height = 32, Scale = scale }) as AvaloniaUISurface;
        if (_ghost is null)
        {
            return;
        }

        _ghost.Accessible = false;
        _ghost.Content = new GhostView { DataContext = label };
        _ghostNode = new UISurfaceNode(_layers.Popups, _ghost, _index) { PreciseDamage = true };
        _ghostNode.Node.InputBox = default;
        MoveGhost(x, y);
    }

    private void MoveGhost(double x, double y)
    {
        if (_ghost is null || _ghostNode is null)
        {
            return;
        }

        var left = (int)x - 20;
        var top = (int)y - 16;
        _ghost.SetPosition(left, top);
        _ghostNode.SetPosition(left, top);
        _outputs.ScheduleAll();
    }

    private void EndTabDragGhost()
    {
        _ghostNode?.Dispose();
        _ghostNode = null;
        _ghost?.Dispose();
        _ghost = null;
    }
}
