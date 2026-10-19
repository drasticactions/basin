using Basin.Desktop;
using Basin.Hosted;

namespace Basin.Shell.Nested;

public sealed partial class NestedShell
{
    private PointerConstraintDriver? _constraintDriver;
    private PointerGesturesManager? _gestures;
    private TabletManager? _tablets;
    private Action<TabletManager.TabletTool, TabletManager.TabletDevice, Capabilities.TabletToolAxes>? _onToolProximity;
    private Action<TabletManager.TabletTool, Capabilities.TabletToolAxes>? _onToolMoved;
    private bool _pinching;
    private RelativePointerManager? _relativePointer;
    private Action<PointerConstraint?, PointerConstraint?>? _onConstraintChanged;

    public bool PointerLocked => _constraintDriver is { IsLocked: true };

    private void AttachPointerConstraints()
    {
        _gestures = _host.Services.Find<PointerGesturesManager>();
        if (_host.Services.Find<TabletManager>() is { } tablets)
        {
            _tablets = tablets;
            _onToolProximity = (tool, _, axes) => AimTool(tool, axes);
            _onToolMoved = AimTool;
            tablets.ToolProximityIn += _onToolProximity;
            tablets.ToolMoved += _onToolMoved;
        }

        _relativePointer = _host.Services.Find<RelativePointerManager>();
        if (_host.Services.Find<PointerConstraintsManager>()?.Driver is { } driver)
        {
            _constraintDriver = driver;
            _onConstraintChanged = OnConstraintChanged;
            driver.Changed += _onConstraintChanged;
        }
    }

    private void DetachPointerConstraints()
    {
        if (_constraintDriver is { } driver && _onConstraintChanged is { } changed)
        {
            driver.Changed -= changed;
        }

        _constraintDriver = null;
        if (_tablets is { } tablets)
        {
            tablets.ToolProximityIn -= _onToolProximity;
            tablets.ToolMoved -= _onToolMoved;
            _tablets = null;
        }

        _host.RequestPointerLock(false);
    }

    private void AimTool(TabletManager.TabletTool tool, Capabilities.TabletToolAxes axes) =>
        tool.AimAt(_host.Scene, _host.Layout, View.Output, axes);

    private void OnPinch(BasinViewInputKind kind, uint time, double scale, bool canceled)
    {
        if (_gestures is not { } gestures)
        {
            return;
        }

        switch (kind)
        {
            case BasinViewInputKind.GesturePinchBegin:
                _pinching = true;
                gestures.NotifyPinchBegin(time, 2);
                break;
            case BasinViewInputKind.GesturePinchUpdate when _pinching:
                gestures.NotifyPinchUpdate(time, 0, 0, scale, 0);
                break;
            case BasinViewInputKind.GesturePinchEnd when _pinching:
                _pinching = false;
                gestures.NotifyPinchEnd(time, canceled);
                break;
        }
    }

    private bool GatePointer(uint time, ref double x, ref double y, bool relative)
    {
        var dx = x - _cursorX;
        var dy = y - _cursorY;
        if (relative && (dx != 0 || dy != 0))
        {
            _relativePointer?.NotifyMotion((ulong)time * 1000, dx, dy, dx, dy);
        }

        if (_constraintDriver is not { } driver)
        {
            return true;
        }

        if (driver.IsLocked)
        {
            return false;
        }

        driver.Constrain(_cursorX, _cursorY, ref x, ref y);
        return true;
    }

    private void OnRelative(uint time, double dx, double dy, double unacceleratedDx, double unacceleratedDy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        _relativePointer?.NotifyMotion((ulong)time * 1000, dx, dy, unacceleratedDx, unacceleratedDy);
        if (_constraintDriver is { IsLocked: true })
        {
            return;
        }

        var x = _cursorX + dx;
        var y = _cursorY + dy;
        _constraintDriver?.Constrain(_cursorX, _cursorY, ref x, ref y);
        MoveCursorTo(x, y, time);
    }

    private void OnConstraintChanged(PointerConstraint? previous, PointerConstraint? next)
    {
        if (previous is { Kind: ConstraintKind.Lock } && _constraintDriver is { } driver &&
            driver.ReleasePoint(previous, out var hintX, out var hintY))
        {
            MoveCursorTo(hintX, hintY, (uint)Environment.TickCount);
            _host.RequestPointerWarp(hintX, hintY);
        }

        _host.RequestPointerLock(next is { Kind: ConstraintKind.Lock });
    }
}
