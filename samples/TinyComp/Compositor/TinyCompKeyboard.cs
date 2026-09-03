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

        if (!fromInputMethod && HandleHyprShortcut(key, pressed))
        {
            return;
        }

        if (pressed && !_sessionLock.IsLocked && !_shortcutsInhibit.IsActive(_seat.Keyboard.Focus)
            && HandleKeybind(key))
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

    private void WireStdin() => _stdinCommands = new StdinCommands(_loop, HandleCommand);

    private void HandleCommand(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case ["move", var x, var y]:
                MoveCursor(double.Parse(x), double.Parse(y), (uint)Environment.TickCount);
                break;
            case ["button", var code, var state]:
                OnButton((uint)Environment.TickCount, uint.Parse(code), state == "1");
                break;
            case ["key", var code, var state]:
                HandleKey((uint)Environment.TickCount, uint.Parse(code), state == "1");
                break;
            case ["shot", var path]:
                _shotPath = path;
                _shotView = 0;
                _driver.RepaintNow(Views[0]);
                break;
            case ["shot", var path, var index]:
                _shotPath = path;
                _shotView = int.Parse(index);
                _driver.RepaintNow(Views[_shotView]);
                break;
            case ["scale", var viewIndex, var factor]:
                SetOutputScale(Views[int.Parse(viewIndex)], double.Parse(factor));
                break;
            case ["shotraw", var path]:
                DumpPresented(Views[0], path);
                break;
            case ["where"]:
                foreach (var window in _windows)
                {
                    BasinReport.Line($"WIN {window.Toplevel.AppId} {window.X} {window.Y} mode={_mode} scene={(window.SceneSurface is null ? "none" : "yes")}");
                }

                foreach (var xwindow in _xwindows)
                {
                    BasinReport.Line($"XWIN {xwindow.XWin.Class} {xwindow.X} {xwindow.Y} {xwindow.XWin.Width}x{xwindow.XWin.Height} rule={(xwindow.Rule is null ? "none" : "yes")} corners={xwindow.CornerRadius} framed={(xwindow.Frame is null ? "no" : "yes")} minimized={xwindow.Minimized}");
                }

                break;
            case ["clip", var index, var cx, var cy, var cw, var ch]:
                {
                    var target = _windows[int.Parse(index)];
                    if (target.Tree is not null)
                    {
                        target.Tree.ClipBox = new Box(int.Parse(cx), int.Parse(cy), int.Parse(cw), int.Parse(ch));
                        BasinReport.Line($"CLIP {index} {target.Tree.ClipBox}");
                    }
                }

                break;
            case ["tile"]:
                TileWindows();
                break;
            case ["ws"]:
                PrintWorkspaces();
                break;
            case ["ws", "next"]:
                SwitchWorkspace(1);
                break;
            case ["ws", "prev"]:
                SwitchWorkspace(-1);
                break;
            case ["ws", "create"]:
                if (ViewAtCursor() is { } createView)
                {
                    ActivateWorkspace(createView, CreateWorkspace(createView, null, afterActive: true));
                }

                break;
            case ["ws", "create", var wsName]:
                if (ViewAtCursor() is { } namedView)
                {
                    ActivateWorkspace(namedView, CreateWorkspace(namedView, wsName, afterActive: true));
                }

                break;
            case ["ws", "move"]:
                CarryFocusedWindow(1);
                break;
            case ["split", var fraction]:
                SetSplit(double.Parse(fraction));
                break;
            case ["dumpscene"]:
                DumpTree(_scene.Root, 0);
                break;
            case ["stats"]:
                BasinReport.Line($"STATS transactions={_useTransactions} timedout={Transaction.TimedOutCount}");
                BasinReport.Line($"STATS cursor theme={(_cursor.Images?.HasTheme == true ? _cursor.Images.Size.ToString() : "none")} " + $"showing={_cursor.Showing} " + $"on={(_cursor.CursorOutput?.Name ?? "none")}");
                foreach (var view in Views)
                {
                    var so = view.Scene;
                    BasinReport.Line(so is null
                        ? $"STATS {view.Output.Name} full-repaint scale={view.Output.Scale}"
                        : $"STATS {view.Output.Name} scanout={so.ScanoutCommits} composed={so.ComposedCommits} skipped={so.SkippedCommits} direct={so.IsDirectScanout} offload={so.OffloadedLayers}/{so.OffloadCommits} swcursor={_cursor.IsSoftwareOn(view.Output)} scale={view.Output.Scale}");
                    if (so is not null)
                    {
                        foreach (var reason in Enum.GetValues<Basin.Scene.PlaneDeclineReason>())
                        {
                            if (so.DeclinedFor(reason) > 0)
                            {
                                BasinReport.Line($"STATS   declined {reason} {so.DeclinedFor(reason)}");
                            }
                        }
                    }
                }

                break;
            case ["nightlight", "off"]:
                ApplyNightLight(null);
                BasinReport.Line($"NIGHTLIGHT off");
                break;
            case ["nightlight", var kelvin]:
                ApplyNightLight(double.Parse(kelvin));
                BasinReport.Line($"NIGHTLIGHT {kelvin}K");
                break;
            case ["gc"]:
                var now = GC.GetAllocatedBytesForCurrentThread();
                BasinReport.Line($"GC {now - _gcMark} bytes since last mark");
                _gcMark = now;
                break;
            case ["reload"]:
                Reload();
                break;
            case ["bell"]:
                RingBell();
                break;
            case ["xminimize", var index, var state]:
                SetMinimized(_xwindows[int.Parse(index)], state == "1");
                break;
            case ["mark", "undo"]:
                _feedback?.UndoMark();
                ScheduleEffectRepaint();
                break;
            case ["mark", "clear"]:
                _feedback?.ClearMarks();
                ScheduleEffectRepaint();
                break;
            case ["zoom", "in"]:
                _post.Zoom?.ZoomIn();
                _post.Magnifier?.ZoomIn();
                ScheduleEffectRepaint();
                break;
            case ["zoom", "out"]:
                _post.Zoom?.ZoomOut();
                _post.Magnifier?.ZoomOut();
                ScheduleEffectRepaint();
                break;
            case ["zoom", "reset"]:
                _post.Zoom?.Reset();
                _post.Magnifier?.Reset();
                ScheduleEffectRepaint();
                break;
            case ["quit"]:
                _runLoop.Stop();
                break;
        }
    }

    private string? _shotPath;
    private int _shotView;
    private long _gcMark;

    private void DumpPresented(OutputView view, string path)
    {
        BasinReport.Line(SceneScreenshot.WritePresented(view.LastPresentedBuffer, _renderer, path) switch
        {
            ScreenshotOutcome.NoFrame => "SHOTRAW unavailable (nothing presented yet)",
            ScreenshotOutcome.Unreadable => "SHOTRAW unavailable (presented buffer not importable)",
            _ => $"SHOTRAW {path}",
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
        if (SceneScreenshot.Write(_scene, _renderer, path, view.Width, view.Height, SceneOptions(view.Output)))
        {
            BasinReport.Line($"SHOT {path}");
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
