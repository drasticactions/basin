using Basin.Ipc;

namespace LunaWm;

internal sealed partial class LunaGreeter
{
    private IpcLineReport _report => _greeter.Report;

    private void Register(IpcMethodRegistry methods)
    {
        _report.Register(methods, "lunawm/menu", "menu {which} [{state}]", (ref IpcParams p, IpcReply _) =>
        {
            var which = p.GetString("which");
            var close = p.TryGetString("state", out var state) && state == "close";
            if (p.Failed)
            {
                return;
            }

            if (close)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu(which);
            }
        });

        _report.Register(methods, "lunawm/menu-choose", "menu-choose {key...}", (ref IpcParams p, IpcReply _) =>
        {
            var key = p.GetString("key");
            if (!p.Failed && !ChooseMenuRow(key))
            {
                _report.Line($"ERR no menu row {key}");
            }
        });

        _report.Register(methods, "lunawm/keyboard", "keyboard [{state}]", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.TryGetString("state", out var given) ? given : "toggle";
            if (!p.Failed)
            {
                SetKeyboardWanted(state switch
                {
                    "show" => true,
                    "hide" => false,
                    _ => !_model.KeyboardVisible,
                });
                _report.Line($"KEYBOARD {(_model.KeyboardVisible ? "on" : "off")}");
            }
        });

        _report.Register(methods, "lunawm/keyboard-type", "keyboard-type {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (p.Failed || _kbTyper is null)
            {
                return;
            }

            if (!_model.KeyboardVisible)
            {
                _report.Line("KEYBOARD off");
                return;
            }

            _uiDriver.Pump();
            _keyboardPolicy?.NoteTouch();
            var typed = _kbTyper.Type(text);
            _uiDriver.Pump();
            _report.Line($"KBTYPE {(typed ? "ok" : "failed")}");
        });

        _report.Register(methods, "lunawm/keyboard-tap", "keyboard-tap {key}", (ref IpcParams p, IpcReply _) =>
        {
            var key = p.GetString("key");
            if (!p.Failed && _kbTyper is not null)
            {
                _uiDriver.Pump();
                _report.Line($"KBTAP {key} {(_model.KeyboardVisible && _kbTyper.Tap(key) ? "ok" : "failed")}");
                _uiDriver.Pump();
            }
        });

        _report.Register(methods, "lunawm/touch", "touch {x:number} {y:number}", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            if (!p.Failed && _active.Current is { } output)
            {
                var box = _layout.BoxOf(output);
                var (px, py) = (box.X + (box.Width * x), box.Y + (box.Height * y));
                var time = (uint)Environment.TickCount;
                _seat.Injector.TouchDown(time, 31, px, py);
                _seat.Injector.TouchFrame();
                _uiDriver.Pump();
                _seat.Injector.TouchUp(time + 40, 31);
                _seat.Injector.TouchFrame();
                _uiDriver.Pump();
            }
        });

        _report.Register(methods, "lunawm/touchdrag", "touchdrag {x:number} {y:number} {dx:number} {dy:number} [{ms:int}]", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            var dx = p.GetDouble("dx");
            var dy = p.GetDouble("dy");
            var ms = p.TryGetInt("ms", out var given) ? (int)given : 200;
            if (!p.Failed && _active.Current is { } output)
            {
                var box = _layout.BoxOf(output);
                var (px, py) = (box.X + (box.Width * x), box.Y + (box.Height * y));
                var time = (uint)Environment.TickCount;
                _seat.Injector.TouchDown(time, 32, px, py);
                _seat.Injector.TouchFrame();
                for (var step = 1; step <= 10; step++)
                {
                    _seat.Injector.TouchMotion(time + (uint)(ms * step / 10), 32, px + (box.Width * dx * step / 10), py + (box.Height * dy * step / 10));
                    _seat.Injector.TouchFrame();
                    _uiDriver.Pump();
                }

                _seat.Injector.TouchUp(time + (uint)ms, 32);
                _seat.Injector.TouchFrame();
                _uiDriver.Pump();
            }
        });

        _report.Register(methods, "lunawm/locate", "locate {name...}", (ref IpcParams p, IpcReply _) =>
        {
            var name = p.GetString("name");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                _report.Line(Locate(name) is { } at ? $"LOCATE {name} {at.X:0.####} {at.Y:0.####}" : $"LOCATE {name} none");
            }
        });

        _report.Register(methods, "lunawm/keyboard-locate", "keyboard-locate {key}", (ref IpcParams p, IpcReply _) =>
        {
            var key = p.GetString("key");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                _report.Line(LocateKey(key) is { } at ? $"KEYAT {key} {at.X:0.####} {at.Y:0.####}" : $"KEYAT {key} none");
            }
        });

        _report.Register(methods, "lunawm/contrast", "contrast {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                SetHighContrast(state == "on");
            }
        });

        _report.Register(methods, "lunawm/text-scale", "text-scale {scale:number}", (ref IpcParams p, IpcReply _) =>
        {
            var scale = p.GetDouble("scale");
            if (!p.Failed)
            {
                SetTextScale(scale);
            }
        });

        _report.Register(methods, "lunawm/reader", "reader {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (p.Failed)
            {
                return;
            }

            if (state == "on")
            {
                _reader?.Start();
            }
            else
            {
                _reader?.Stop();
            }

            _report.Line($"READERSTATE {(_reader is { IsRunning: true } ? "on" : "off")} available={(_reader is { IsAvailable: true } ? "yes" : "no")}");
        });
    }

    private (double X, double Y)? Locate(string name)
    {
        if (_loginView is not { } view || _active.Current is not { } output)
        {
            return null;
        }

        foreach (var visual in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view))
        {
            if (visual is not Avalonia.Controls.Control { IsEffectivelyVisible: true } control)
            {
                continue;
            }

            var label = Avalonia.Automation.AutomationProperties.GetName(control);
            if (label != name && !(control is Avalonia.Controls.Button { Content: string content } && content == name))
            {
                continue;
            }

            if (Avalonia.VisualExtensions.TranslatePoint(control, new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2), view) is { } center)
            {
                var box = _layout.BoxOf(output);
                return (center.X * _textScale / box.Width, center.Y * _textScale / box.Height);
            }
        }

        return null;
    }

    private (double X, double Y)? LocateKey(string key)
    {
        if (_kb is null || _kbModel is null || _kbTyper is null || !_model.KeyboardVisible || _loginView is { KeyboardPanel.IsSliding: true } || _active.Current is not { } output)
        {
            return null;
        }

        var cell = _kbTyper.Find(key);
        if (cell < 0)
        {
            return null;
        }

        var box = _layout.BoxOf(output);
        var viewHeight = box.Height / _textScale;
        var rect = _kb.Rects[cell];
        var y = viewHeight - _model.KeyboardHeight + _kbModel.KeymapTop + rect.CenterY;
        return (rect.CenterX * _textScale / box.Width, y * _textScale / box.Height);
    }

    private void PrintState()
    {
        _report.Line($"POINTER {_seat.PointerX} {_seat.PointerY}");
        foreach (var output in _outputs.Views)
        {
            _report.Line($"AREA {output.Output.Name} output={_layout.BoxOf(output.Output)} scale={output.Output.Scale}");
        }

        _report.Line($"BOX {_active.Current?.Name ?? "none"} placed={_box.IsPlaced} bounds={_box.Bounds}");
        var view = _model.ShowUsers ? "users" : _model.AskName ? "username" : "conversation";
        _report.Line($"VIEW {view} user={_model.CurrentUser} prompt=\"{_model.PromptText}\" error=\"{_model.ErrorText}\" secret={(_model.SecretShown ? "yes" : "no")}");
        _report.Line($"LOGIN {_flow?.LoginState.ToString().ToLowerInvariant() ?? "none"}");
        _report.Line($"SELECTED session={_flow?.SelectedSession ?? "none"} user={_model.SelectedUser?.Name ?? "none"}");
        _report.Line($"KEYMAP layout={_flow?.SelectedLayout ?? "system"}");
        _report.Line($"ACCESS contrast={(_highContrast ? "on" : "off")} text={SizeOf(_textScale)} surface={_box.Surface?.Size.Width}x{_box.Surface?.Size.Height}@{_box.Surface?.Size.Scale}");
        _report.Line($"KEYBOARD {(_model.KeyboardVisible ? "on" : "off")}");
        PrintMenuState();
        _report.Line($"FOCUS {(_seat.Router.KeyboardFocus is null ? "none" : ReferenceEquals(_seat.Router.KeyboardFocus, _box.Surface) ? "box" : "other")}");
    }
}
