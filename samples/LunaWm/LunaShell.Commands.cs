using System.Globalization;
using Basin;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Ipc;
using Basin.Scene;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;
    private FrameSettle? _settle;
    private string? _shotPath;
    private IpcPendingReply? _shotReply;

    private LunaView CommandView => _views.Count > 0 ? PlacementView() : throw new InvalidOperationException("The shell has no output.");

    private void WireIpc()
    {
        _settle = new FrameSettle(_outputs, _uiDriver);
        if (_inspector is { } inspectorSession)
        {
            inspectorSession.Settle = _settle;
        }

        _ipc = _options.Ipc.Attach(_host.Loop, _services, _host.Socket, new IpcSessionInfo
        {
            Compositor = "luna-wm",
            Backend = _options.Backend.ToString().ToLowerInvariant(),
            Renderer = _rendererName,
            XwaylandDisplay = () => _xServer?.DisplayName,
            Quit = Stop,
            Settle = _settle,
            Processes = _processes,
        });
        _ipc.SyntheticInput = _seat.Injector;
        _ipc.Events.Declare(KeyboardEvent);
        RegisterCommands(_ipc.Methods);
        _inspector?.Register(_ipc.Methods);
        _ipc.Start();
        _ipc.StartLineFront();
    }

    private void UnwireIpc()
    {
        _ipc?.Dispose();
        _ipc = null;
        _settle?.Dispose();
        _settle = null;
    }

    private static IpcMethodInfo Info(string description, string? schema = null, IpcMethodTraits traits = IpcMethodTraits.None) =>
        new(description, schema, traits);

    private void RegisterCommands(IpcMethodRegistry methods)
    {
        _report.AddInputLines(methods);
        _report.AddSessionLines(methods);

        _report.Register(methods, "lunawm/cards", "cards",
            Info("Print one CARD line for every card, in deck order, with its stack, position and pose.", traits: IpcMethodTraits.ReadOnly),
            PrintCards);

        _report.Register(methods, "lunawm/card-view", "card-view [{state}]",
            Info("Toggle the card view on the output under the pointer, or set it with state on or off.",
                """{"type":"object","properties":{"state":{"type":"string","enum":["on","off","toggle"]}}}"""),
            (ref IpcParams p, IpcReply _) =>
            {
                var state = p.TryGetString("state", out var given) ? given : "toggle";
                var view = CommandView;
                switch (state)
                {
                    case "on":
                        EnterCardView(view);
                        break;
                    case "off":
                        Maximize(view);
                        break;
                    default:
                        ToggleCardView(view);
                        break;
                }
            });

        _report.Register(methods, "lunawm/gesture", "gesture {action}",
            Info("Run a gesture action: card_view, launcher, quick_launch, maximize, back, next, previous, home, dashboard, lock, close or keyboard.",
                """{"type":"object","properties":{"action":{"type":"string","enum":["card_view","launcher","quick_launch","maximize","back","next","previous","home","dashboard","lock","close","keyboard","inspector"]}},"required":["action"]}"""),
            (ref IpcParams p, IpcReply reply) =>
            {
                var action = p.GetString("action");
                if (p.Failed)
                {
                    return;
                }

                if (!LunaBindings.IsAction(action))
                {
                    reply.Error(IpcErrorCodes.InvalidParams, $"Gesture action '{action}' does not exist. Give card_view, launcher, home or another action that the method lists.");
                    return;
                }

                RunAction(CommandView, action);
            });

        _report.Register(methods, "lunawm/launcher", "launcher [{state}]",
            Info("Open the launcher, or close it with state close.",
                """{"type":"object","properties":{"state":{"type":"string","enum":["open","close"]}}}"""),
            (ref IpcParams p, IpcReply _) =>
            {
                var close = p.TryGetString("state", out var state) && state == "close";
                ShowLauncher(CommandView, !close);
            });

        _report.Register(methods, "lunawm/notify", "notify {summary} [{body...}]",
            Info("Post a notification through Basin.Notifications without D-Bus, and print its id.",
                """{"type":"object","properties":{"summary":{"type":"string"},"body":{"type":"string"}},"required":["summary"]}"""),
            (ref IpcParams p, IpcReply _) =>
            {
                var summary = p.GetString("summary");
                var body = p.TryGetString("body", out var given) ? given : "";
                if (!p.Failed)
                {
                    PostNotification(summary, body);
                }
            });

        _report.Register(methods, "lunawm/lock", "lock",
            Info("Lock the session with luna-wm's own lock screen.", traits: IpcMethodTraits.Idempotent),
            Lock);

        _report.Register(methods, "lunawm/maximize", "maximize {id:int}", (ref IpcParams p, IpcReply reply) =>
        {
            var id = (int)p.GetInt("id");
            if (!p.Failed && CardById(id, reply) is { } card)
            {
                Show(card);
            }
        });

        _report.Register(methods, "lunawm/close", "close {id:int}", (ref IpcParams p, IpcReply reply) =>
        {
            var id = (int)p.GetInt("id");
            if (!p.Failed && CardById(id, reply) is { } card)
            {
                CloseCard(card);
            }
        });

        _report.Register(methods, "lunawm/flick", "flick {id:int} {vy:number}", (ref IpcParams p, IpcReply reply) =>
        {
            var id = (int)p.GetInt("id");
            var velocity = p.GetDouble("vy");
            if (p.Failed || CardById(id, reply) is not { Home: { } view } card)
            {
                return;
            }

            if (!view.InCardView)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "Flick works only in the card view. Open the card view first.");
                return;
            }

            if (velocity < CloseVelocity)
            {
                FlickClose(view, card, velocity);
            }
            else
            {
                _report.Line($"FLICK {card.Id} back");
            }
        });

        _report.Register(methods, "lunawm/reorder", "reorder {id:int} {stack:int} [{pos:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var id = (int)p.GetInt("id");
            var stack = (int)p.GetInt("stack");
            var position = p.TryGetInt("pos", out var given) ? (int)given : int.MaxValue;
            if (p.Failed || CardById(id, reply) is not { Home: { } view } card)
            {
                return;
            }

            if (stack >= view.Deck.Stacks.Count)
            {
                view.Deck.MoveToNewStack(card, view.Deck.Stacks.Count);
            }
            else
            {
                view.Deck.MoveToStack(card, Math.Max(0, stack), Math.Min(position, view.Deck.Stacks[Math.Max(0, stack)].Count));
            }

            LayoutCards(view, animate: true, spec: LunaMotion.CardShuffleReorder);
            _report.Line($"REORDER {card.Id} stack={view.Deck.ActiveIndex}");
        });

        _report.Register(methods, "lunawm/send", "send {id:int} {output}", (ref IpcParams p, IpcReply reply) =>
        {
            var id = (int)p.GetInt("id");
            var output = p.GetString("output");
            if (p.Failed || CardById(id, reply) is not { Home: { } from } card)
            {
                return;
            }

            foreach (var view in _views)
            {
                if (view.Output.Name == output && !ReferenceEquals(view, from))
                {
                    MoveCardToView(card, from, view);
                    return;
                }
            }

            reply.Error(IpcErrorCodes.NotFound, $"Output '{output}' does not exist or already holds the card. Give the name of a different output.");
        });

        _report.Register(methods, "lunawm/stack", "stack {id:int} {state}", (ref IpcParams p, IpcReply reply) =>
        {
            var id = (int)p.GetInt("id");
            var state = p.GetString("state");
            if (p.Failed || CardById(id, reply) is not { Home: { } view } card)
            {
                return;
            }

            if (state == "open")
            {
                view.Deck.Activate(card);
                view.Scroll = 0;
                LayoutCards(view, animate: true);
                _report.Line($"STACK open {view.Deck.ActiveIndex}");
            }
            else
            {
                var index = view.Deck.ActiveIndex;
                view.Deck.ActivateStack(index + 1 < view.Deck.Stacks.Count ? index + 1 : index - 1);
                LayoutCards(view, animate: true);
                _report.Line($"STACK close {index}");
            }
        });

        _report.Register(methods, "lunawm/edge", "edge {side} {progress:number} [{hold}] [{ms:int}]", (ref IpcParams p, IpcReply _) =>
        {
            var side = p.GetString("side");
            var progress = p.GetDouble("progress");
            var hold = p.TryGetInt("ms", out var ms) ? (int)ms : 0;
            if (!p.Failed)
            {
                EdgeCommand(side, progress, hold);
            }
        });

        _report.Register(methods, "lunawm/touch", "touch {x:number} {y:number}", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            if (!p.Failed)
            {
                var view = CommandView;
                _seat.TapAt(view.Box.X + (view.Box.Width * x), view.Box.Y + (view.Box.Height * y));
            }
        });

        _report.Register(methods, "lunawm/touchdrag", "touchdrag {x:number} {y:number} {dx:number} {dy:number} [{ms:int}]", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            var dx = p.GetDouble("dx");
            var dy = p.GetDouble("dy");
            var ms = p.TryGetInt("ms", out var given) ? (int)given : 200;
            if (!p.Failed)
            {
                var view = CommandView;
                _seat.DragTouch(
                    view.Box.X + (view.Box.Width * x), view.Box.Y + (view.Box.Height * y),
                    view.Box.Width * dx, view.Box.Height * dy, ms);
            }
        });

        _report.Register(methods, "lunawm/pinch", "pinch {x:number} {y:number} {from:number} {to:number}", (ref IpcParams p, IpcReply _) =>
        {
            var x = p.GetDouble("x");
            var y = p.GetDouble("y");
            var from = p.GetDouble("from");
            var to = p.GetDouble("to");
            if (!p.Failed)
            {
                PinchCommand(x, y, from, to);
            }
        });

        _report.Register(methods, "lunawm/launcher-tab", "launcher-tab {name...}", (ref IpcParams p, IpcReply _) =>
        {
            var name = p.GetString("name");
            if (!p.Failed)
            {
                LauncherTabCommand(CommandView, name);
            }
        });

        _report.Register(methods, "lunawm/launcher-launch", "launcher-launch {name...}", (ref IpcParams p, IpcReply reply) =>
        {
            var name = p.GetString("name");
            if (p.Failed)
            {
                return;
            }

            var view = CommandView;
            if (view.LauncherUi?.Model.Find(name) is { } icon)
            {
                ShowLauncher(view, true);
                view.LauncherUi.Model.Launch(icon);
            }
            else
            {
                reply.Error(IpcErrorCodes.NotFound, $"The launcher has no icon '{name}'.");
            }
        });

        _report.Register(methods, "lunawm/quick-launch", "quick-launch [{state}]", (ref IpcParams p, IpcReply _) =>
        {
            var close = p.TryGetString("state", out var state) && state == "close";
            ShowQuickLaunch(CommandView, !close);
        });

        _report.Register(methods, "lunawm/type", "type {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed)
            {
                TypeCommand(CommandView, text);
            }
        });

        _report.Register(methods, "lunawm/just-type-choose", "just-type-choose {index:int}", (ref IpcParams p, IpcReply _) =>
        {
            var index = (int)p.GetInt("index");
            if (!p.Failed)
            {
                JustTypeChoose(CommandView, index);
            }
        });

        _report.Register(methods, "lunawm/dashboard", "dashboard [{state}]", (ref IpcParams p, IpcReply _) =>
        {
            var close = p.TryGetString("state", out var state) && state == "close";
            ShowDashboard(CommandView, !close);
        });

        _report.Register(methods, "lunawm/banner-end", "banner-end", EndBanner);

        _report.Register(methods, "lunawm/dismiss", "dismiss {id:int}", (ref IpcParams p, IpcReply _) =>
        {
            var id = (uint)p.GetInt("id");
            if (!p.Failed)
            {
                DismissNotification(id);
            }
        });

        _report.Register(methods, "lunawm/menu", "menu {which} [{state}]", (ref IpcParams p, IpcReply _) =>
        {
            var which = p.GetString("which");
            var close = p.TryGetString("state", out var state) && state == "close";
            if (!p.Failed)
            {
                SystemMenuCommand(CommandView, which, !close);
            }
        });

        _report.Register(methods, "lunawm/appmenu", "appmenu", PrintAppMenu);

        _report.Register(methods, "lunawm/wifi-join", "wifi-join {ssid...}", (ref IpcParams p, IpcReply _) =>
        {
            var ssid = p.GetString("ssid");
            if (!p.Failed)
            {
                if (_status?.Network?.AccessPoints.FirstOrDefault(point => point.Ssid == ssid) is { } point)
                {
                    _status.Join(point);
                }
                else
                {
                    _report.Line($"WIFI join {ssid} unknown");
                }
            }
        });

        _report.Register(methods, "lunawm/wifi-passphrase", "wifi-passphrase {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed)
            {
                foreach (var view in _views)
                {
                    if (view.Menu is { Open: true, Kind: "wifi" } menu && menu.Model.Rows.OfType<PassphraseRow>().FirstOrDefault() is { } row)
                    {
                        row.Text = text;
                        row.Submit();
                        return;
                    }
                }

                _report.Line("WIFI passphrase none");
            }
        });

        _report.Register(methods, "lunawm/appmenu-choose", "appmenu-choose {path...}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                AppMenuChoose(path);
            }
        });

        _report.Register(methods, "lunawm/unlock-type", "unlock-type {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed)
            {
                UnlockType(text);
            }
        });

        _report.Register(methods, "lunawm/unlock-submit", "unlock-submit", UnlockSubmit);

        _report.Register(methods, "lunawm/clock", "clock {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed)
            {
                SetClockOverride(text);
                _report.Line($"CLOCK {text}");
            }
        });

        _report.Register(methods, "lunawm/shot", "shot {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                SettleAll();
                WriteScreenshot(path);
                _report.Line($"SHOT {path}");
            }
        });

        _report.Register(methods, "lunawm/shotraw", "shotraw {path}", (ref IpcParams p, IpcReply reply) =>
        {
            var path = p.GetString("path");
            if (p.Failed || _views.Count == 0)
            {
                return;
            }

            _uiDriver.Pump();
            _shotPath = path;
            _shotReply = reply.Defer();
            _outputs.ScheduleAll();
            _outputs.RepaintNow(_views[0].Driver);
        });

        _report.Register(methods, "lunawm/alloc-probe", "alloc-probe {path} {frames:int}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            var frames = (int)p.GetInt("frames");
            if (!p.Failed)
            {
                AllocationProbe(path, Math.Clamp(frames, 1, 100_000));
            }
        });

        RegisterKeyboardCommands(methods);
        _report.Register(methods, "lunawm/where", "where", PrintState);
        _report.Register(methods, "lunawm/reload", "reload", Reload);
        _report.Register(methods, "lunawm/cursor", "cursor", () => _report.Line($"CURSOR {_seat.CursorState}"));
        _report.Register(methods, "lunawm/launch", "launch {command...}", (ref IpcParams p, IpcReply _) =>
        {
            var command = p.GetString("command");
            if (!p.Failed)
            {
                Launch(command);
            }
        });
    }

    private Card? CardById(int id, IpcReply reply)
    {
        foreach (var card in _cards)
        {
            if (card.Id == id)
            {
                return card;
            }
        }

        reply.Error(IpcErrorCodes.NotFound, $"Card {id} does not exist. Run where to see the cards.");
        return null;
    }

    private void SettleAll()
    {
        foreach (var view in _views)
        {
            foreach (var stack in view.Deck)
            {
                foreach (var card in stack)
                {
                    card.Settle();
                }
            }

            foreach (var card in _dying)
            {
                card.Settle();
            }

            SettleChrome(view);
        }

        _uiDriver.Pump();
    }

    private void PrintCards()
    {
        foreach (var view in _views)
        {
            _report.Line($"VIEW {view.Output.Name} {(view.InCardView ? "cardview" : "maximized")} stacks={view.Deck.Stacks.Count} active={view.Deck.ActiveIndex}");
            for (var s = 0; s < view.Deck.Stacks.Count; s++)
            {
                var stack = view.Deck.Stacks[s];
                for (var c = 0; c < stack.Count; c++)
                {
                    var card = stack.Cards[c];
                    var pose = card.Target;
                    var active = ReferenceEquals(card, view.Deck.ActiveCard) ? " active" : "";
                    _report.Line(string.Create(CultureInfo.InvariantCulture,
                        $"CARD {card.Id} {card.AppId} stack={s} pos={c} x={pose.X:0} y={pose.Y:0} scale={pose.Scale:0.###} rot={pose.Rotation:0.##} dim={pose.Brightness:0.##}{active}{(card.Fullscreen ? " fullscreen" : "")} title={card.Title}"));
                }
            }
        }
    }

    private void PrintState()
    {
        _report.Line($"POINTER {_seat.PointerX:0.##} {_seat.PointerY:0.##}");
        foreach (var view in _views)
        {
            var box = view.Box;
            _report.Line($"OUTPUT {view.Output.Name} {box.X},{box.Y},{box.Width}x{box.Height} scale={view.Output.Scale:0.###} mode={(view.InCardView ? "cardview" : "maximized")} bar={(view.BarHidden && !view.BarRevealed ? "hidden" : "shown")} title={view.Bar?.Model.Title}");
            PrintChromeState(view);
        }

        _report.Line($"LOCK {(_locked ? "on" : "off")}{(_sessionLocked ? " session" : "")}");
        _report.Line($"FOCUS {(Focused is { } focused ? focused.Id.ToString(CultureInfo.InvariantCulture) : "-")}");
        _report.Line($"SURFACES {_chromeIndex.Count}");
    }

    private void TakeShot(OutputView driver)
    {
        if (_shotPath is not { } path || _views.Count == 0 || !ReferenceEquals(_views[0].Driver, driver))
        {
            return;
        }

        _shotPath = null;
        var buffer = driver.LastPresentedBuffer;
        var line = CompositorLines.ShotRaw(SceneScreenshot.WritePresented(buffer, _renderer, path), buffer, path);
        if (_shotReply is { } pending)
        {
            _shotReply = null;
            _ = IpcLineReport.Complete(pending, line);
        }
        else
        {
            _report.Line(line);
        }
    }

    private IEventSource? _edgeRelease;

    private void EdgeCommand(string side, double progress, int holdMs)
    {
        var view = CommandView;
        var box = view.Box;
        var band = _config.EdgeBand / 2.0;
        var (x0, y0, x1, y1) = side switch
        {
            "left" => (box.X + band, box.Y + (box.Height / 2.0), box.X + band + (box.Width * progress), box.Y + (box.Height / 2.0)),
            "right" => (box.Right - band, box.Y + (box.Height / 2.0), box.Right - band - (box.Width * progress), box.Y + (box.Height / 2.0)),
            "top" => (box.X + (box.Width / 2.0), box.Y + band, box.X + (box.Width / 2.0), box.Y + band + (box.Height * progress)),
            "along" => (box.X + (box.Width * 0.75), box.Bottom - band, box.X + (box.Width * 0.75) - (box.Width * progress), box.Bottom - band),
            _ => (box.X + (box.Width / 2.0), box.Bottom - band, box.X + (box.Width / 2.0), box.Bottom - band - (box.Height * progress)),
        };
        var time = (uint)Environment.TickCount;
        const int steps = 10;
        _seat.TouchDown(7, x0, y0, time);
        for (var i = 1; i <= steps; i++)
        {
            time += 16;
            _seat.TouchMotion(7, x0 + ((x1 - x0) * i / steps), y0 + ((y1 - y0) * i / steps), time);
        }

        if (holdMs <= 0)
        {
            _seat.TouchUp(7, time + 16);
            return;
        }

        _edgeRelease?.Remove();
        _edgeRelease = _host.Loop.AddTimer(() =>
        {
            _edgeRelease?.Remove();
            _edgeRelease = null;
            _seat.TouchUp(7, (uint)Environment.TickCount);
        });
        _edgeRelease.UpdateTimer(holdMs);
    }

    private void PinchCommand(double x, double y, double from, double to)
    {
        var view = CommandView;
        var cx = view.Box.X + (view.Box.Width * x);
        var cy = view.Box.Y + (view.Box.Height * y);
        var half = view.Box.Width / 2.0;
        var time = (uint)Environment.TickCount;
        const int steps = 12;
        _seat.TouchDown(5, cx - (half * from), cy, time);
        _seat.TouchDown(6, cx + (half * from), cy, time);
        for (var i = 1; i <= steps; i++)
        {
            time += 16;
            var spread = half * (from + ((to - from) * i / steps));
            _seat.TouchMotion(5, cx - spread, cy, time);
            _seat.TouchMotion(6, cx + spread, cy, time);
        }

        _seat.TouchUp(5, time + 16);
        _seat.TouchUp(6, time + 16);
    }
}
