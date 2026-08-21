using Basin;
using Basin.Shell.Xdg;
using Microsoft.Extensions.Logging;

namespace Westonia;

internal sealed partial class Westonia
{
    private const uint BtnLeft = 0x110;
    private const uint BtnRight = 0x111;
    private const uint BtnMiddle = 0x112;

    private readonly ShellModifierState _modifiers = new();
    private ShellModifiers _bindingModifier = ShellModifiers.Super;

    private bool OnKeyBinding(uint time, uint key, bool pressed)
    {
        if (_modifiers.Track(key, pressed))
        {
            if (!pressed && _switcher is { IsOpen: true } && !_modifiers.Holds(_bindingModifier))
            {
                _switcher.Commit();
                return true;
            }

            return false;
        }

        if (!pressed)
        {
            return false;
        }

        if (_lock is { IsLocked: true, ClientLocked: false })
        {
            if (key is EvdevKeys.Escape or 28)
            {
                _lock.Unlock();
                return true;
            }

            return _shell.KeyboardTarget is null;
        }

        if (_ini.Shell.AllowZap &&
            _modifiers.Exactly(ShellModifiers.Ctrl | ShellModifiers.Alt) &&
            key == EvdevKeys.Backspace)
        {
            _log.LogInformation("terminating on the zap binding");
            Stop();
            return true;
        }

        switch (key)
        {
            case EvdevKeys.BrightnessUp:
                StepBacklight(+1);
                return true;
            case EvdevKeys.BrightnessDown:
                StepBacklight(-1);
                return true;
        }

        if (_bindingModifier == ShellModifiers.None || !_modifiers.Holds(_bindingModifier))
        {
            return false;
        }

        var shift = _modifiers.Holds(ShellModifiers.Shift);
        var ctrl = _modifiers.Holds(ShellModifiers.Ctrl);

        switch (key)
        {
            case EvdevKeys.Tab:
                OpenSwitcher();
                return true;
            case EvdevKeys.K:
                _shell.Kill(_shell.Focused);
                return true;
            case EvdevKeys.F9:
                StepBacklight(-1);
                return true;
            case EvdevKeys.F10:
                StepBacklight(+1);
                return true;
            case EvdevKeys.M when shift:
                _shell.ToggleMaximized(_shell.Focused);
                return true;
            case EvdevKeys.F when shift:
                _shell.ToggleFullscreen(_shell.Focused);
                return true;
            case EvdevKeys.Left when shift:
                _shell.SetTiledOrientation(_shell.Focused, ResizeEdges.Left);
                return true;
            case EvdevKeys.Right when shift:
                _shell.SetTiledOrientation(_shell.Focused, ResizeEdges.Right);
                return true;
            case EvdevKeys.Up when shift:
                _shell.SetTiledOrientation(_shell.Focused, ResizeEdges.Top);
                return true;
            case EvdevKeys.Down when shift:
                _shell.SetTiledOrientation(_shell.Focused, ResizeEdges.Bottom);
                return true;
        }

        if (_switcher is { IsOpen: true })
        {
            switch (key)
            {
                case EvdevKeys.Tab when shift:
                    _switcher.Previous();
                    return true;
                case EvdevKeys.Escape:
                    _switcher.Cancel();
                    return true;
            }
        }

        return HandleWorkspaceBinding(key, shift, ctrl);
    }

    private bool OnButtonBinding(uint time, uint button, bool pressed)
    {
        if (!pressed)
        {
            if (_shell.Grab.Active)
            {
                _shell.EndGrab();
                return true;
            }

            return false;
        }

        var pointer = (_seat!.PointerX, _seat.PointerY);
        var window = WindowUnder(pointer.PointerX, pointer.PointerY);

        if (_bindingModifier != ShellModifiers.None && _modifiers.Holds(_bindingModifier) && window is not null)
        {
            var shift = _modifiers.Holds(ShellModifiers.Shift);
            switch (button)
            {
                case BtnLeft when !shift:
                    _shell.Focus(window);
                    _shell.BeginMove(window, pointer.PointerX, pointer.PointerY, clientInitiated: false);
                    return true;
                case BtnRight:
                case BtnLeft when shift:
                    _shell.Focus(window);
                    _shell.BeginResize(window, EdgeFor(window, pointer.PointerX, pointer.PointerY), pointer.PointerX, pointer.PointerY);
                    return true;
                case BtnMiddle:
                    _log.LogInformation("rotation is not implemented: this compositor has no surface rotation");
                    return true;
            }
        }

        if (button is BtnLeft or BtnRight && window is not null)
        {
            _shell.Focus(window);
        }

        return false;
    }

    private static ResizeEdges EdgeFor(ShellWindow window, double x, double y)
    {
        var geometry = window.Geometry;
        var edges = ResizeEdges.None;
        edges |= x < geometry.X + (geometry.Width / 2) ? ResizeEdges.Left : ResizeEdges.Right;
        edges |= y < geometry.Y + (geometry.Height / 2) ? ResizeEdges.Top : ResizeEdges.Bottom;
        return edges;
    }

    private ShellWindow? WindowUnder(double x, double y) =>
        _scene.SurfaceAt(x, y) is { Surface: { } surface } ? _shell.WindowOwning(surface) : null;

    private bool OnFrameButton(double x, double y, uint button, bool pressed)
    {
        if (_shell.Grab.Active)
        {
            return false;
        }

        var window = FrameUnder(x, y);
        if (window?.Frame is not { } frame)
        {
            return false;
        }

        if (!pressed)
        {
            if (button == BtnLeft && frame.HitsClose(x, y))
            {
                window.Window.Close();
                return true;
            }

            return false;
        }

        _shell.Focus(window);

        if (button == BtnLeft && frame.HitsClose(x, y))
        {
            return true;
        }

        if (button == BtnLeft && frame.EdgeAt(x, y) is var edges && edges != ResizeEdges.None)
        {
            _shell.BeginResize(window, edges, x, y);
            return true;
        }

        if (button == BtnLeft && frame.HitsTitlebar(x, y))
        {
            _shell.BeginMove(window, x, y, clientInitiated: true);
            return true;
        }

        return false;
    }

    private string? FrameCursorName(double x, double y)
    {
        if (FrameUnder(x, y)?.Frame is not { } frame)
        {
            return null;
        }

        var edges = frame.EdgeAt(x, y);
        if (edges != ResizeEdges.None)
        {
            return edges switch
            {
                ResizeEdges.Left => "w-resize",
                ResizeEdges.Right => "e-resize",
                ResizeEdges.Top => "n-resize",
                ResizeEdges.Bottom => "s-resize",
                ResizeEdges.TopLeft => "nw-resize",
                ResizeEdges.TopRight => "ne-resize",
                ResizeEdges.BottomLeft => "sw-resize",
                ResizeEdges.BottomRight => "se-resize",
                _ => "default",
            };
        }

        return frame.HitsTitlebar(x, y) ? "default" : null;
    }

    private ShellWindow? FrameUnder(double x, double y)
    {
        if (_seat?.Hovered is { } hovered)
        {
            foreach (var window in _shell.Windows)
            {
                if (window.Frame?.OwnsSurface(hovered) == true)
                {
                    return window;
                }
            }

            return null;
        }

        foreach (var window in _shell.Windows)
        {
            if (window.Frame is { } frame && frame.AcceptsInputAt(x, y))
            {
                return window;
            }
        }

        return null;
    }

    private void OpenSwitcher() => _switcher?.Open();

    private bool HandleWorkspaceBinding(uint key, bool shift, bool ctrl)
    {
        if (_workspaces is not { } workspaces || workspaces.Count < 2 || shift)
        {
            return false;
        }

        switch (key)
        {
            case EvdevKeys.Up when ctrl:
                Carry(workspaces.Active - 1);
                return true;
            case EvdevKeys.Down when ctrl:
                Carry(workspaces.Active + 1);
                return true;
            case EvdevKeys.Up:
                workspaces.Activate(workspaces.Active - 1);
                RefocusForWorkspace();
                return true;
            case EvdevKeys.Down:
                workspaces.Activate(workspaces.Active + 1);
                RefocusForWorkspace();
                return true;
        }

        if (key >= EvdevKeys.F1 && key < EvdevKeys.F1 + 6)
        {
            var index = (int)(key - EvdevKeys.F1);
            if (index < workspaces.Count)
            {
                workspaces.Activate(index);
                RefocusForWorkspace();
                return true;
            }
        }

        return false;
    }

    private void Carry(int target)
    {
        if (_workspaces is not { } workspaces || _shell.Focused is not { } window)
        {
            return;
        }

        var index = ((target % workspaces.Count) + workspaces.Count) % workspaces.Count;
        workspaces.Carry(window, index);
        workspaces.Activate(index);
        _shell.Focus(window);
    }

    private void RefocusForWorkspace()
    {
        if (_workspaces is not { } workspaces)
        {
            return;
        }

        foreach (var window in _shell.Windows)
        {
            if (window.Workspace == workspaces.Active && window.Window.IsMapped &&
                window.Kind != ShellWindowKind.Minimized)
            {
                _shell.Focus(window);
                return;
            }
        }

        Seat.Keyboard.NotifyClearFocus();
    }

    private void StepBacklight(int direction) =>
        _log.LogInformation(
            "backlight {Direction} is bound and does nothing: basin has no panel-brightness capability",
            direction > 0 ? "up" : "down");
}
