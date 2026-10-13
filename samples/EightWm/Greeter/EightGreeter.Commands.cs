using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Basin.Ipc;
using Basin.SoftKeyboard.Ipc;

namespace EightWm;

internal sealed partial class EightGreeter
{
    private IpcLineReport Lines => _greeter.Report;

    private void Register(IpcMethodRegistry methods)
    {
        var report = Lines;
        report.Register(methods, "eightwm/curtain", "curtain {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (p.Failed)
            {
                return;
            }

            _uiDriver.Pump();
            if (state == "down")
            {
                LowerCurtain();
            }
            else
            {
                RaiseCurtain("command");
            }

            _uiDriver.Pump();
        });

        report.Register(methods, "eightwm/options", "options", () =>
        {
            _signIn.ToggleOptions.Execute(null);
            report.Line($"OPTIONS {(_signIn.OptionsOpen ? "open" : "closed")} sessions={_signIn.Sessions.Count} layouts={_signIn.Layouts.Count}");
        });

        report.Register(methods, "eightwm/ease", "ease {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                _signIn.EaseOpen = state == "open";
                report.Line($"EASE {(_signIn.EaseOpen ? "open" : "closed")}");
            }
        });

        report.Register(methods, "eightwm/power", "power {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (p.Failed)
            {
                return;
            }

            if (state is "suspend" or "restart" or "shutdown")
            {
                Power(state);
                return;
            }

            _signIn.PowerOpen = state == "open" && _signIn.PowerAvailable;
            report.Line($"POWERMENU {(_signIn.PowerOpen ? "open" : "closed")}");
        });

        report.Register(methods, "eightwm/reveal", "reveal {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                _signIn.Revealed = state == "on";
                _uiDriver.Pump();
                report.Line($"REVEAL {(_signIn.Revealed ? "on" : "off")} allowed={(_signIn.RevealAllowed ? "yes" : "no")}");
            }
        });

        report.Register(methods, "eightwm/network", "network {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                _network.Open = state == "open" && _network.CanJoin;
                report.Line($"NETWORKMENU {(_network.Open ? "open" : "closed")} points={_network.Points.Count}");
            }
        });

        report.Register(methods, "eightwm/network-join", "network-join {ssid...}", (ref IpcParams p, IpcReply _) =>
        {
            var ssid = p.GetString("ssid");
            if (!p.Failed)
            {
                Join(ssid);
            }
        });

        report.Register(methods, "eightwm/network-passphrase", "network-passphrase {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                _seat.Router.TextCommit(text);
                _uiDriver.Pump();
                _network.Connect.Execute(null);
            }
        });

        report.Register(methods, "eightwm/wifi", "wifi {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                _network.WifiEnabled = state == "on";
            }
        });

        report.Register(methods, "eightwm/contrast", "contrast {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                SetHighContrast(state == "on");
            }
        });

        report.Register(methods, "eightwm/text-scale", "text-scale {scale:number}", (ref IpcParams p, IpcReply _) =>
        {
            var scale = p.GetDouble("scale");
            if (!p.Failed)
            {
                SetTextScale(scale);
            }
        });

        report.Register(methods, "eightwm/reader", "reader {state}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (p.Failed)
            {
                return;
            }

            if (state == "on")
            {
                _greeter.Reader?.Start();
            }
            else
            {
                _greeter.Reader?.Stop();
            }

            report.Line($"READERSTATE {(_greeter.Reader is { IsRunning: true } ? "on" : "off")} available={(_greeter.Reader is { IsAvailable: true } ? "yes" : "no")}");
        });

        report.Register(methods, "eightwm/touch", "touch {x:number} {y:number}", (ref IpcParams p, IpcReply _) =>
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

        report.Register(methods, "eightwm/touchdrag", "touchdrag {x:number} {y:number} {dx:number} {dy:number} [{ms:int}]", (ref IpcParams p, IpcReply _) =>
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

        report.Register(methods, "eightwm/repaint", "repaint", () =>
        {
            Repaint(_screen);
            foreach (var pair in _backgrounds.Values)
            {
                Repaint(pair);
            }

            _uiDriver.Pump();
        });

        report.Register(methods, "eightwm/locate", "locate {name...}", (ref IpcParams p, IpcReply _) =>
        {
            var name = p.GetString("name");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                report.Line(Locate(name) is { } at ? $"LOCATE {name} {at.X:0.####} {at.Y:0.####}" : $"LOCATE {name} none");
            }
        });

        report.Register(methods, "eightwm/keyboard-locate", "keyboard-locate {key}", (ref IpcParams p, IpcReply _) =>
        {
            var key = p.GetString("key");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                report.Line(LocateKey(key) is { } at ? $"KEYAT {key} {at.X:0.####} {at.Y:0.####}" : $"KEYAT {key} none");
            }
        });

        if (_keyboard is { } keyboard)
        {
            SoftKeyboardSessionIpc.Register(methods, report, keyboard, new SoftKeyboardIpcHooks
            {
                Namespace = "eightwm",
                IsShown = () => _keyboardVisible,
                Show = () => SetKeyboardWanted(true),
                Hide = _ => SetKeyboardWanted(false),
                Toggle = () => SetKeyboardWanted(!_keyboardVisible),
                Report = () => report.Line($"KEYBOARD {(_keyboardVisible ? "on" : "off")} layout={keyboard.CurrentLayout.Id}"),
            });
        }
    }

    private static void Repaint(Visual? root)
    {
        if (root is null)
        {
            return;
        }

        root.InvalidateVisual();
        foreach (var visual in root.GetVisualDescendants())
        {
            visual.InvalidateVisual();
        }
    }

    private (double X, double Y)? Locate(string name)
    {
        if (_screen is not { } screen || screen.KeyboardPanel.IsSliding || screen.SignIn.OptionsMoving || _active.Current is not { } output)
        {
            return null;
        }

        foreach (var visual in screen.GetVisualDescendants())
        {
            if (visual is not Control { IsEffectivelyVisible: true } control)
            {
                continue;
            }

            var label = AutomationProperties.GetName(control);
            if (label != name && !(control is Button { Content: string content } && content == name))
            {
                continue;
            }

            if (control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), screen) is { } center)
            {
                var box = _layout.BoxOf(output);
                return (center.X * _textScale / box.Width, center.Y * _textScale / box.Height);
            }
        }

        return null;
    }

    private (double X, double Y)? LocateKey(string key)
    {
        if (_keyboard is not { } keyboard || _screen is not { } screen || !_keyboardVisible || screen.KeyboardPanel.IsSliding || _active.Current is not { } output)
        {
            return null;
        }

        var cell = keyboard.Typer.Find(key);
        if (cell < 0 || screen.Keyboard.TranslatePoint(default, screen) is not { } origin)
        {
            return null;
        }

        var rect = keyboard.Engine.Rects[cell];
        var box = _layout.BoxOf(output);
        var x = origin.X + rect.CenterX;
        var y = origin.Y + keyboard.Engine.Padding + rect.CenterY;
        return (x * _textScale / box.Width, y * _textScale / box.Height);
    }

    private void PrintState()
    {
        var report = Lines;
        report.Line($"POINTER {_seat.PointerX} {_seat.PointerY}");
        foreach (var view in _outputs.Views)
        {
            report.Line($"AREA {view.Output.Name} output={_layout.BoxOf(view.Output)} scale={view.Output.Scale}");
        }

        report.Line($"BOX {_active.Current?.Name ?? "none"} placed={_box.IsPlaced} bounds={_box.Bounds}");
        report.Line($"CURTAIN {(_curtain.Raised ? "up" : "down")}");
        report.Line($"VIEW {(_signIn.ShowList ? "users" : _signIn.AskName ? "username" : "password")} user={_signIn.SelectedUser?.Name ?? "none"} prompt=\"{_signIn.Prompt}\" error=\"{_signIn.Error}\" busy={(_signIn.Busy ? "yes" : "no")}");
        report.Line($"LOGIN {_flow?.LoginState.ToString().ToLowerInvariant() ?? "none"}");
        report.Line($"ACCESS contrast={(_highContrast ? "on" : "off")} text={_textScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} reveal={(_signIn.RevealAllowed ? "allowed" : "off")}");
        report.Line($"FOCUS {(_seat.Router.KeyboardFocus is null ? "none" : ReferenceEquals(_seat.Router.KeyboardFocus, _box.Surface) ? "box" : "other")}");
        report.Line($"KEYBOARD {(_keyboardVisible ? "on" : "off")} layout={_keyboard?.CurrentLayout.Id ?? "none"}");
        report.Line($"POPUPS {_uiDriver.Popups.Count}");
    }
}
