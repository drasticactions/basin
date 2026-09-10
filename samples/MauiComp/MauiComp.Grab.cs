using Basin;
using Basin.Shell.Xdg;

namespace MauiComp;

internal sealed partial class MauiComp
{
    private CursorMode _cursorMode = CursorMode.Passthrough;
    private ShellWindow? _grabbed;
    private double _grabX;
    private double _grabY;
    private Box _grabGeometry;
    private ResizeEdges _grabEdges;
    private ShellRunDialog? _draggedDialog;

    internal bool IsGrabbing => _cursorMode != CursorMode.Passthrough || _draggedDialog is not null;

    private void BeginDialogDrag(ShellRunDialog dialog)
    {
        if (_seat is null)
        {
            return;
        }

        _draggedDialog = dialog;
        _grabX = _seat.PointerX - dialog.X;
        _grabY = _seat.PointerY - dialog.Y;
    }

    private void BeginInteractive(ShellWindow window, CursorMode mode, ResizeEdges edges)
    {
        if (_seat is null || !ReferenceEquals(window, _focused))
        {
            return;
        }

        _grabbed = window;
        _cursorMode = mode;
        window.RestorePending = false;
        var x = _seat.PointerX;
        var y = _seat.PointerY;
        if (mode == CursorMode.Move)
        {
            _grabX = x - window.Tree.X;
            _grabY = y - window.Tree.Y;
            return;
        }

        var geometry = window.Window.Xdg.EffectiveGeometry;
        var borderX = window.Tree.X + geometry.X + (edges.HasFlag(ResizeEdges.Right) ? geometry.Width : 0);
        var borderY = window.Tree.Y + geometry.Y + (edges.HasFlag(ResizeEdges.Bottom) ? geometry.Height : 0);
        _grabX = x - borderX;
        _grabY = y - borderY;
        _grabGeometry = new Box(window.Tree.X + geometry.X, window.Tree.Y + geometry.Y, geometry.Width, geometry.Height);
        _grabEdges = edges;
        window.Window.SetResizing(true);
    }

    private void ContinueInteractive(double x, double y)
    {
        if (_draggedDialog is { } dialog)
        {
            dialog.MoveTo((int)(x - _grabX), (int)(y - _grabY));
            return;
        }

        if (_grabbed is not { } window)
        {
            return;
        }

        if (_cursorMode == CursorMode.Move)
        {
            window.Tree.SetPosition((int)(x - _grabX), (int)(y - _grabY));
            window.Titlebar?.Update(_outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0);
            _outputs.ScheduleAll();
            return;
        }

        var borderX = x - _grabX;
        var borderY = y - _grabY;
        var left = _grabGeometry.X;
        var right = _grabGeometry.X + _grabGeometry.Width;
        var top = _grabGeometry.Y;
        var bottom = _grabGeometry.Y + _grabGeometry.Height;
        if (_grabEdges.HasFlag(ResizeEdges.Top))
        {
            top = Math.Min((int)borderY, bottom - 1);
        }
        else if (_grabEdges.HasFlag(ResizeEdges.Bottom))
        {
            bottom = Math.Max((int)borderY, top + 1);
        }

        if (_grabEdges.HasFlag(ResizeEdges.Left))
        {
            left = Math.Min((int)borderX, right - 1);
        }
        else if (_grabEdges.HasFlag(ResizeEdges.Right))
        {
            right = Math.Max((int)borderX, left + 1);
        }

        var geometry = window.Window.Xdg.EffectiveGeometry;
        window.Tree.SetPosition(left - geometry.X, top - geometry.Y);
        window.Window.SetSize(right - left, bottom - top);
        _outputs.ScheduleAll();
    }

    private void ResetCursorMode()
    {
        _grabbed?.Window.SetResizing(false);
        _cursorMode = CursorMode.Passthrough;
        _grabbed = null;
        _draggedDialog = null;
    }
}
