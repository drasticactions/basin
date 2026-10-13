using Basin.Greetd;
using Basin.Ipc;
using BasinGreeter.UI;

namespace BasinGreeter;

internal sealed partial class Greeter
{
    private (long Bytes, long Frames, long Ticks) _allocMark;

    private IpcLineReport _report => _greeter.Report;

    private void Register(IpcMethodRegistry methods)
    {
        _report.Register(methods, "greeter/alloc-mark", "alloc-mark", () =>
        {
            _allocMark = (GC.GetAllocatedBytesForCurrentThread(), _outputs.PrimaryRendered, Environment.TickCount64);
            _report.Line("ALLOCMARK");
        });

        _report.Register(methods, "greeter/alloc-since", "alloc-since", () =>
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread() - _allocMark.Bytes;
            var frames = _outputs.PrimaryRendered - _allocMark.Frames;
            _report.Line($"ALLOCSINCE frames={frames} bytes={bytes} ms={Environment.TickCount64 - _allocMark.Ticks}");
        });

        _report.Register(methods, "greeter/network", "network {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                _model.Bar.Network.Open = state == "open" && _model.Bar.Network.CanJoin;
                _report.Line($"NETWORKMENU {(_model.Bar.Network.Open ? "open" : "closed")} points={_model.Bar.Network.Points.Count}");
            }
        });

        _report.Register(methods, "greeter/network-join", "network-join {ssid...}", (ref IpcParams p, IpcReply _) =>
        {
            var ssid = p.GetString("ssid");
            if (!p.Failed)
            {
                Join(ssid);
            }
        });

        _report.Register(methods, "greeter/network-passphrase", "network-passphrase {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                _seat.Router.TextCommit(text);
                _uiDriver.Pump();
                _model.Bar.Network.Connect.Execute(null);
            }
        });

        _report.Register(methods, "greeter/wifi", "wifi {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                _model.Bar.Network.WifiEnabled = state == "on";
            }
        });

        _report.Register(methods, "greeter/keyboard", "keyboard {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
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

        _report.Register(methods, "greeter/keyboard-type", "keyboard-type {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (p.Failed || _keyboard is null)
            {
                return;
            }

            if (!_model.KeyboardVisible)
            {
                _report.Line("KEYBOARD off");
                return;
            }

            _uiDriver.Pump();
            var typed = _keyboard.TypeText(text);
            _uiDriver.Pump();
            _report.Line($"KBTYPE {(typed ? "ok" : "failed")}");
        });

        _report.Register(methods, "greeter/keyboard-tap", "keyboard-tap {key}", (ref IpcParams p, IpcReply _) =>
        {
            var key = p.GetString("key");
            if (!p.Failed && _keyboard is not null)
            {
                _uiDriver.Pump();
                _report.Line($"KBTAP {key} {(_keyboard.Tap(key) ? "ok" : "failed")}");
                _uiDriver.Pump();
            }
        });

        _report.Register(methods, "greeter/contrast", "contrast {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                SetHighContrast(state == "on");
            }
        });

        _report.Register(methods, "greeter/text-scale", "text-scale {scale:number}", (ref IpcParams p, IpcReply _) =>
        {
            var scale = p.GetDouble("scale");
            if (!p.Failed)
            {
                SetTextScale(scale);
            }
        });

        _report.Register(methods, "greeter/accessibility", "accessibility {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                _model.Accessibility.Open = state == "open";
                _report.Line($"ACCESSIBILITY {(_model.Accessibility.Open ? "open" : "closed")}");
            }
        });

        _report.Register(methods, "greeter/reader", "reader {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (p.Failed)
            {
                return;
            }

            if (state == "on")
            {
                StartReader();
            }
            else
            {
                StopReader();
            }

            _report.Line($"READERSTATE {(_greeter.Reader is { IsRunning: true } ? "on" : "off")} available={(_greeter.Reader is { IsAvailable: true } ? "yes" : "no")}");
        });

        _report.Register(methods, "greeter/power", "power {action}", (ref IpcParams p, IpcReply _) =>
        {
            var action = p.GetString("action");
            if (!p.Failed)
            {
                Power(action);
            }
        });

        _report.Register(methods, "greeter/power-ask", "power-ask {action}", (ref IpcParams p, IpcReply _) =>
        {
            var action = p.GetString("action");
            if (p.Failed)
            {
                return;
            }

            switch (action)
            {
                case "restart":
                    _model.Bar.Restart.Execute(null);
                    break;
                case "shutdown":
                    _model.Bar.ShutDown.Execute(null);
                    break;
                case "confirm":
                    _model.Bar.Confirm.Execute(null);
                    break;
                case "cancel":
                    _model.Bar.CancelConfirm.Execute(null);
                    break;
            }

            _report.Line($"CONFIRM {(_model.Bar.Confirming ? _model.Bar.ConfirmAction : "none")}");
        });
    }

    private void PrintState()
    {
        _report.Line($"POINTER {_seat.PointerX} {_seat.PointerY}");
        foreach (var view in _outputs.Views)
        {
            _report.Line($"AREA {view.Output.Name} output={_layout.BoxOf(view.Output)} scale={view.Output.Scale}");
        }

        _report.Line($"BOX {_active.Current?.Name ?? "none"} placed={_box.IsPlaced} bounds={_box.Bounds}");
        _report.Line($"VIEW {(_model.ShowList ? "users" : _model.AskUsername ? "username" : "conversation")} user={_model.CurrentUser} prompt=\"{_model.PromptText}\" error=\"{_model.ErrorText}\"");
        _report.Line($"LOGIN {_flow?.LoginState.ToString().ToLowerInvariant() ?? "none"}");
        _report.Line($"SELECTED session={_model.SelectedSession?.Id ?? "none"} user={_model.SelectedUser?.Name ?? "none"}");
        _report.Line($"ACCESS contrast={(_highContrast ? "on" : "off")} text={AccessibilityModel.SizeOf(_textScale)} surface={_box.Surface?.Size.Width}x{_box.Surface?.Size.Height}@{_box.Surface?.Size.Scale}");
        _report.Line($"KEYMAP layout={_flow?.SelectedLayout ?? "system"}");
        _report.Line($"FOCUS {(_seat.Router.KeyboardFocus is null ? "none" : ReferenceEquals(_seat.Router.KeyboardFocus, _box.Surface) ? "box" : "other")}");
        _report.Line($"POPUPS {_uiDriver.Popups.Count}");
    }
}
