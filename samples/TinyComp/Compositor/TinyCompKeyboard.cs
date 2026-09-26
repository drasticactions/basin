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
using Basin.Ipc;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private void WireKeyboard(WaylandKeyboardDevice keyboard)
    {
        keyboard.Keymap += bytes => _seat.Keyboard.SetKeymapFromBuffer(bytes);
        keyboard.RepeatInfo += (rate, delay) => _seat.Keyboard.SetRepeatInfo(rate, delay);
        keyboard.Modifiers += (d, l, k, g) =>
        {
            _seat.Keyboard.Activate(null);
            _seat.Keyboard.NotifyModifiers(d, l, k, g);
        };
        keyboard.Key += (time, key, pressed) =>
        {
            _seat.Keyboard.Activate(null);
            HandleKey(time, key, pressed);
        };
    }

    private void HandleKey(uint time, uint key, bool pressed, bool fromInputMethod = false)
    {
        if (pressed && key == InputCodes.KeyEsc && _openMenu is not null)
        {
            DismissOpenMenu();
            return;
        }

        if (RouteUIKey(time, key, pressed))
        {
            return;
        }

        if (!fromInputMethod && HandleGlobalShortcut(key, pressed))
        {
            return;
        }

        if (pressed && !_sessionLock.IsLocked && !_shortcutsInhibit.IsActive(_seat.Keyboard.Focus)
            && HandleKeybind(key))
        {
            return;
        }

        if (!_sessionLock.IsLocked && OverviewKey(key, pressed))
        {
            return;
        }

        if (_effects.SwitcherActive && !_sessionLock.IsLocked)
        {
            if (!pressed && ReleasesSwitcher(key))
            {
                EndSwitcher(focus: true);
            }
            else
            {
                if (pressed && key == InputCodes.KeyEsc)
                {
                    EndSwitcher(focus: false);
                }

                return;
            }
        }

        if (!fromInputMethod && _textInput.HasKeyboardGrab)
        {
            _textInput.ForwardKey(time, key, pressed);
            return;
        }

        _seat.Keyboard.NotifyKey(time, key, pressed);
    }

    private string? _shotPath;
    private int _shotView;
    private long _gcMark;

    private void DumpPlanes(OutputView view, string prefix)
    {
        var chrome = new List<Basin.Host.PlaneShotChrome>();
        foreach (var window in _windows)
        {
            if (window.Frame?.PresentedChrome is { IsDestroyed: false } presented)
            {
                chrome.Add(new Basin.Host.PlaneShotChrome(
                    $"frame-{window.Toplevel.AppId}", presented, Detail: $"outer={window.Frame.OuterBounds}"));
            }
        }

        _ = Basin.Host.PlaneShot.Write(view, _renderer, prefix, _scene, chrome);
    }

    private void DumpPresented(OutputView view, string path)
    {
        var blit = view.Output is IHardwareCursor hardware &&
            hardware.TryPresentedCursor(out var sprite, out var where)
            ? new CursorBlit(sprite, where)
            : default;
        var outcome = SceneScreenshot.WritePresented(
            view.LastPresentedBuffer, view.Scene?.PresentedLayers, blit, _renderer, path, out var planes);
        _report.Line(outcome switch
        {
            ScreenshotOutcome.NoFrame => "SHOTRAW unavailable (nothing presented yet)",
            ScreenshotOutcome.Unreadable => "SHOTRAW unavailable (presented buffer not importable)",
            _ => $"SHOTRAW {path} planes={planes}",
        });
    }

    private void MaybeScreenshot(OutputView view)
    {
        if (_shotPath is null || view != Views[_shotView])
        {
            return;
        }

        var path = _shotPath;
        _shotPath = null;
        var written = SceneScreenshot.Write(_scene, _renderer, path, view.Width, view.Height, SceneOptions(view.Output));
        if (_shotReply is { } pending)
        {
            _shotReply = null;
            _ = written ? IpcLineReport.Complete(pending, $"SHOT {path}") : IpcLineReport.Complete(pending);
        }
        else if (written)
        {
            _report.Line($"SHOT {path}");
        }
    }

    private bool IsAltDown() => _seat.Keyboard.State?.IsModActive("Mod1") == true;

    private bool IsShiftDown() => _seat.Keyboard.State?.IsModActive("Shift") == true;

    private Basin.Config.Modifiers _switcherModifiers;

    private Basin.Config.Modifiers HeldModifiers()
    {
        var state = _seat.Keyboard.State;
        if (state is null)
        {
            return Basin.Config.Modifiers.None;
        }

        var held = Basin.Config.Modifiers.None;
        if (state.IsModActive("Shift"))
        {
            held |= Basin.Config.Modifiers.Shift;
        }

        if (state.IsModActive("Control"))
        {
            held |= Basin.Config.Modifiers.Ctrl;
        }

        if (state.IsModActive("Mod1"))
        {
            held |= Basin.Config.Modifiers.Mod1;
        }

        if (state.IsModActive("Mod3"))
        {
            held |= Basin.Config.Modifiers.Mod3;
        }

        if (state.IsModActive("Mod4"))
        {
            held |= Basin.Config.Modifiers.Mod4;
        }

        if (state.IsModActive("Mod5"))
        {
            held |= Basin.Config.Modifiers.Mod5;
        }

        return held;
    }

    private bool ReleasesSwitcher(uint key)
    {
        if (_switcherModifiers == Basin.Config.Modifiers.None)
        {
            return false;
        }

        var released = ModifierOf(_seat.Keyboard.RawKeysymFor(key).Value);
        return released != Basin.Config.Modifiers.None && (_switcherModifiers & released) != 0;
    }

    private static Basin.Config.Modifiers ModifierOf(uint keysym) => keysym switch
    {
        AltLeft or AltRight or MetaLeft or MetaRight => Basin.Config.Modifiers.Mod1,
        SuperLeft or SuperRight => Basin.Config.Modifiers.Mod4,
        ControlLeft or ControlRight => Basin.Config.Modifiers.Ctrl,
        ShiftLeft or ShiftRight => Basin.Config.Modifiers.Shift,
        _ => Basin.Config.Modifiers.None,
    };

    private const uint ShiftLeft = 0xffe1;
    private const uint ShiftRight = 0xffe2;
    private const uint ControlLeft = 0xffe3;
    private const uint ControlRight = 0xffe4;
    private const uint MetaLeft = 0xffe7;
    private const uint MetaRight = 0xffe8;
    private const uint AltLeft = 0xffe9;
    private const uint AltRight = 0xffea;
    private const uint SuperLeft = 0xffeb;
    private const uint SuperRight = 0xffec;

    private bool HandleKeybind(uint key)
    {
        var symbol = _seat.Keyboard.RawKeysymFor(key).Value;
        if (symbol == Basin.Config.Keysym.NoSymbol)
        {
            return false;
        }

        var held = HeldModifiers();
        foreach (var binding in _config.Bindings)
        {
            if (binding.Keysym != symbol || binding.ModifierMask != held)
            {
                continue;
            }

            if (binding.Command is { Length: > 0 } command)
            {
                Spawn(command);
                return true;
            }

            if (binding.Action is { } action && RunAction(action, binding.ModifierMask))
            {
                return true;
            }
        }

        return false;
    }

    private void Spawn(string[] command)
    {
        try
        {
            Basin.Diagnostics.BasinDiagnostics.StartClient(string.Join(' ', command), _socket)?.Dispose();
            _feedback?.OnSpawn(EffectTick());
        }
        catch (Exception e)
        {
            _log.Error($"spawn failed: {e.Message}");
        }
    }

    private bool RunAction(KeyAction action, Basin.Config.Modifiers modifiers)
    {
        switch (action)
        {
            case KeyAction.Quit:
                _runLoop.Stop();
                return true;

            case KeyAction.Cycle when _effects.SwitcherEnabled:
            case KeyAction.Switcher:
                if (_effects.SwitcherActive
                    || (CurrentWorkspace() is { } cards && WorkspaceWindowCount(cards) > 1))
                {
                    _switcherModifiers = modifiers;
                    AdvanceSwitcher();
                    return true;
                }

                return false;

            case KeyAction.Cycle:
            case KeyAction.CycleFocus:
                if (CurrentWorkspace() is { } cycle && WorkspaceWindowCount(cycle) > 1)
                {
                    CycleWorkspaceFocus(cycle);
                    return true;
                }

                return false;

            case KeyAction.CycleScale:
                CycleScale();
                return true;

            case KeyAction.CarryNext:
                CarryFocusedWindow(1);
                return true;

            case KeyAction.CarryPrev:
                CarryFocusedWindow(-1);
                return true;

            case KeyAction.WorkspaceNext:
                SwitchWorkspace(1);
                return true;

            case KeyAction.WorkspacePrev:
                SwitchWorkspace(-1);
                return true;

            case KeyAction.WorkspaceNew when ViewAtCursor() is { } view:
                ActivateWorkspace(view, CreateWorkspace(view, null, afterActive: true));
                return true;

            case KeyAction.ZoomIn:
                _post.Zoom?.ZoomIn();
                _post.Magnifier?.ZoomIn();
                ScheduleEffectRepaint();
                return true;

            case KeyAction.ZoomOut:
                _post.Zoom?.ZoomOut();
                _post.Magnifier?.ZoomOut();
                ScheduleEffectRepaint();
                return true;

            case KeyAction.ZoomReset:
                _post.Zoom?.Reset();
                _post.Magnifier?.Reset();
                ScheduleEffectRepaint();
                return true;

            case KeyAction.MarkUndo:
                _feedback?.UndoMark();
                ScheduleEffectRepaint();
                return true;

            case KeyAction.MarkClear:
                _feedback?.ClearMarks();
                ScheduleEffectRepaint();
                return true;

            case KeyAction.Bell:
                RingBell();
                return true;

            case KeyAction.CanvasToggle:
                SetCanvasEnabled(!CanvasActiveAnywhere());
                return true;

            case KeyAction.ParkLeft when FocusedGrabTarget() is { } parkLeft:
                Park(parkLeft, CanvasSide.Left);
                return true;

            case KeyAction.ParkRight when FocusedGrabTarget() is { } parkRight:
                Park(parkRight, CanvasSide.Right);
                return true;

            case KeyAction.ParkUp when FocusedGrabTarget() is { } parkUp:
                Park(parkUp, CanvasSide.Top);
                return true;

            case KeyAction.ParkDown when FocusedGrabTarget() is { } parkDown:
                Park(parkDown, CanvasSide.Bottom);
                return true;

            case KeyAction.Recall when FocusedGrabTarget() is { } recall:
                Recall(recall);
                return true;

            case KeyAction.ShelfSmaller:
                AdjustShelf(null, null, -1, reset: false);
                return true;

            case KeyAction.ShelfLarger:
                AdjustShelf(null, null, 1, reset: false);
                return true;

            case KeyAction.ShelfReset:
                AdjustShelf(null, null, 0, reset: true);
                return true;

            case KeyAction.CanvasMode:
                _ = SetCanvasMode(null);
                return true;

            case KeyAction.Overview:
                _ = ToggleOverview(null, null);
                return true;

            case KeyAction.ShelveLeft:
                ShelveFocused(CanvasSide.Left);
                return true;

            case KeyAction.ShelveRight:
                ShelveFocused(CanvasSide.Right);
                return true;

            case KeyAction.ShelveTop:
                ShelveFocused(CanvasSide.Top);
                return true;

            case KeyAction.ShelveBottom:
                ShelveFocused(CanvasSide.Bottom);
                return true;

            case KeyAction.Unshelve:
                UnshelveByKey();
                return true;

            case KeyAction.Settings:
                if (ToggleSettings() is { } unavailable)
                {
                    _report.Line($"SETTINGS unavailable {unavailable}");
                }

                return true;

            default:
                return false;
        }
    }

    private void CycleWorkspaceFocus(Workspace workspace)
    {
        var members = new List<Window>();
        foreach (var window in _windows)
        {
            if (window.Workspace == workspace && !window.Minimized)
            {
                members.Add(window);
            }
        }

        if (members.Count == 0)
        {
            return;
        }

        var index = _focused is null ? 0 : (members.IndexOf(_focused) + 1) % members.Count;
        FocusWindow(members[index]);
    }

    private readonly List<IGrabTarget> _switcherWindows = [];
    private SceneRect? _switcherDim;

    private bool SwitcherCardLive(IGrabTarget card) => card switch
    {
        Window window => _windows.Contains(window) && window.Tree is { IsDestroyed: false },
        XWindow xwindow => _xwindows.Contains(xwindow) && !xwindow.Tree.IsDestroyed,
        _ => false,
    };

    private void AdvanceSwitcher()
    {
        if (!_effects.SwitcherActive)
        {
            if (AnyOverviewActive())
            {
                _report.Line("SWITCHER refused: overview");
                return;
            }

            var workspace = CurrentWorkspace();
            _switcherWindows.Clear();
            foreach (var window in _windows)
            {
                if (window.Workspace == workspace && !window.Minimized && window.Tree is { IsDestroyed: false })
                {
                    _switcherWindows.Add(window);
                }
            }

            foreach (var xwindow in _xwindows)
            {
                if (xwindow.Workspace == workspace && xwindow.Framable && !xwindow.Minimized
                    && !xwindow.Tree.IsDestroyed)
                {
                    _switcherWindows.Add(xwindow);
                }
            }

            if (_switcherWindows.Count < 2)
            {
                _switcherWindows.Clear();
                return;
            }

            var output = _layout.OutputAt(_cursorX, _cursorY) ?? Views[0].Output;
            var box = _layout.BoxOf(output);
            var focused = _focused is not null ? _switcherWindows.IndexOf(_focused)
                : _focusedX is not null ? _switcherWindows.IndexOf(_focusedX)
                : -1;
            var start = (focused + 1) % _switcherWindows.Count;
            _switcherDim = new SceneRect(workspace?.Tree ?? _layers.Windows, box.Width, box.Height, new RenderColor(0f, 0f, 0f, 0.45f));
            _switcherDim.SetPosition(box.X, box.Y);
            var trees = new List<SceneTree>(_switcherWindows.Count);
            foreach (var card in _switcherWindows)
            {
                trees.Add(card.EffectTree!);
            }

            SuspendCanvas();
            _effects.SwitcherBegin(trees, box, start);
            RestackSwitcher();
            return;
        }

        var next = _effects.SwitcherSelected;
        for (var step = 0; step < _switcherWindows.Count; step++)
        {
            next = (next + 1) % _switcherWindows.Count;
            if (SwitcherCardLive(_switcherWindows[next]))
            {
                break;
            }
        }

        _effects.SwitcherSelect(next);
        RestackSwitcher();
        HighlightSwitcherCards();
    }

    private void HighlightSwitcherCards()
    {
        if (!_effects.HighlightEnabled)
        {
            return;
        }

        var selected = _effects.SwitcherSelected;
        for (var i = 0; i < _switcherWindows.Count; i++)
        {
            _effects.SetHighlight(_switcherWindows[i].EffectTree, i == selected);
        }
    }

    private void RestackSwitcher()
    {
        _switcherDim?.RaiseToTop();
        var selected = _effects.SwitcherSelected;
        for (var distance = _switcherWindows.Count - 1; distance >= 0; distance--)
        {
            for (var i = 0; i < _switcherWindows.Count; i++)
            {
                if (Math.Abs(i - selected) == distance && _switcherWindows[i].EffectTree is { IsDestroyed: false } tree)
                {
                    tree.RaiseToTop();
                }
            }
        }
    }

    private void EndSwitcher(bool focus)
    {
        if (!_effects.SwitcherActive)
        {
            return;
        }

        var selected = _effects.SwitcherSelected;
        _effects.ClearHighlights();
        _effects.SwitcherEnd();
        ResumeCanvas();
        _switcherDim?.Destroy();
        _switcherDim = null;
        if (focus && selected >= 0 && selected < _switcherWindows.Count)
        {
            switch (_switcherWindows[selected])
            {
                case Window window when _windows.Contains(window):
                    FocusWindow(window);
                    break;

                case XWindow xwindow when _xwindows.Contains(xwindow):
                    FocusXWindow(xwindow);
                    break;
            }
        }

        _switcherWindows.Clear();
    }

    private void DropSwitcherCard(IGrabTarget card)
    {
        if (!_effects.SwitcherActive || !_switcherWindows.Contains(card))
        {
            return;
        }

        foreach (var candidate in _switcherWindows)
        {
            if (candidate != card && SwitcherCardLive(candidate))
            {
                return;
            }
        }

        EndSwitcher(focus: false);
    }
}
