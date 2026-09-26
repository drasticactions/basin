using Basin;
using Basin.Capabilities;
using Basin.Ipc;
using Basin.Scene;
using Wayland;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;
    private IpcPendingReply? _shotReply;

    private void WireIpc(Basin.Cli.IpcChoice choice, string backend, string renderer)
    {
        _ipc = choice.Attach(_loop, _services, _socket, new IpcSessionInfo
        {
            Compositor = "tinycomp",
            Backend = backend,
            Renderer = renderer,
            XwaylandDisplay = () => _xwayland?.DisplayName,
            Quit = () => _runLoop.Stop(),
        });
        _ipc.SyntheticInput = new SyntheticInput(this);
        RegisterCommands(_ipc.Methods);
        RegisterOverviewCommands(_ipc.Methods);
        RegisterSettingsCommands(_ipc.Methods);
        DeclareOverviewEvents();
    }

    private void StartIpc()
    {
        if (_ipc is not { IsStarted: false } ipc)
        {
            return;
        }

        ipc.Start();
        ipc.StartLineFront();
    }

    private void RegisterCommands(IpcMethodRegistry methods)
    {
        _report.AddLine(methods, IpcMethodNames.InputPointerMove, "move {x:number} {y:number}");
        _report.AddLine(methods, IpcMethodNames.InputPointerButton, "button {button:int} {pressed:bool}");
        _report.AddLine(methods, IpcMethodNames.InputKey, "key {code:int} {pressed:bool}");
        _report.AddLine(methods, IpcMethodNames.SessionQuit, "quit");

        _report.Register(methods, "tinycomp/shot", "shot {path} [{index:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var path = p.GetString("path");
            var index = p.TryGetInt("index", out var at) ? (int)at : 0;
            if (p.Failed || !View(index, reply, out var view))
            {
                return;
            }

            if (_shotReply is { } superseded)
            {
                _shotReply = null;
                _ = IpcLineReport.Complete(superseded);
            }

            _shotPath = path;
            _shotView = index;
            _shotReply = reply.Defer();
            _driver.RepaintNow(view);
        });

        _report.Register(methods, "tinycomp/scale", "scale {view:int} {factor:number}", (ref IpcParams p, IpcReply reply) =>
        {
            var index = (int)p.GetInt("view");
            var factor = p.GetDouble("factor");
            if (!p.Failed && View(index, reply, out var view))
            {
                SetOutputScale(view, factor);
            }
        });

        _report.Register(methods, "tinycomp/shotraw", "shotraw {path} [{index:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var path = p.GetString("path");
            var index = p.TryGetInt("index", out var at) ? (int)at : 0;
            if (!p.Failed && View(index, reply, out var view))
            {
                DumpPresented(view, path);
            }
        });

        _report.Register(methods, "tinycomp/planeshot", "planeshot {prefix}", (ref IpcParams p, IpcReply reply) =>
        {
            var prefix = p.GetString("prefix");
            if (!p.Failed && View(0, reply, out var view))
            {
                DumpPlanes(view, prefix);
            }
        });

        _report.Register(methods, "tinycomp/where", "where", () =>
        {
            foreach (var window in _windows)
            {
                _report.Line($"WIN {window.Toplevel.AppId} {window.X} {window.Y} mode={_mode} scene={(window.SceneSurface is null ? "none" : "yes")} screen={ScreenBoxOf(window).X}");
                ReportCanvasWhere(window);
                if (ViewOfWindow(window) is { Tag: OutputPolicy, Canvas.Overview: true } || IsShelved(window))
                {
                    _report.Line(OverviewWhere(window));
                }
            }

            foreach (var xwindow in _xwindows)
            {
                _report.Line($"XWIN {xwindow.XWin.Class} {xwindow.X} {xwindow.Y} {xwindow.XWin.Width}x{xwindow.XWin.Height} rule={(xwindow.Rule is null ? "none" : "yes")} corners={xwindow.CornerRadius} framed={(xwindow.Frame is null ? "no" : "yes")} minimized={xwindow.Minimized}");
            }
        });

        _report.Register(methods, "tinycomp/clip", "clip {index:int} {x:int} {y:int} {width:int} {height:int}", (ref IpcParams p, IpcReply reply) =>
        {
            var index = (int)p.GetInt("index");
            var box = new Box((int)p.GetInt("x"), (int)p.GetInt("y"), (int)p.GetInt("width"), (int)p.GetInt("height"));
            if (p.Failed)
            {
                return;
            }

            if (index < 0 || index >= _windows.Count)
            {
                reply.Error(IpcErrorCodes.NotFound, $"no window {index}");
                return;
            }

            var target = _windows[index];
            if (target.Tree is not null)
            {
                target.Tree.ClipBox = box;
                _report.Line($"CLIP {index} {target.Tree.ClipBox}");
            }
        });

        _report.Register(methods, "tinycomp/tile", "tile", TileWindows);

        _report.Register(methods, "tinycomp/canvas", "canvas {state:on|off}", (ref IpcParams p, IpcReply _) =>
        {
            var state = p.GetString("state");
            if (!p.Failed)
            {
                SetCanvasEnabled(state == "on");
            }
        });

        _report.Register(methods, "tinycomp/park", "park {side:left|right|up|down}", (ref IpcParams p, IpcReply reply) =>
        {
            var side = p.GetString("side") switch
            {
                "left" => CanvasSide.Left,
                "right" => CanvasSide.Right,
                "up" => CanvasSide.Top,
                "down" => CanvasSide.Bottom,
                _ => CanvasSide.None,
            };
            if (p.Failed)
            {
                return;
            }

            if (side == CanvasSide.None)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "side is left, right, up or down");
                return;
            }

            if (FocusedGrabTarget() is { } window)
            {
                Park(window, side);
            }
        });

        _report.Register(methods, "tinycomp/recall", "recall", () =>
        {
            if (FocusedGrabTarget() is { } window)
            {
                Recall(window);
            }
        });

        _report.Register(methods, "tinycomp/shelf", "shelf", (ref IpcParams p, IpcReply reply) =>
        {
            CanvasSide? side = null;
            if (p.TryGetString("side", out var sideName))
            {
                side = sideName switch
                {
                    "left" => CanvasSide.Left,
                    "right" => CanvasSide.Right,
                    "top" => CanvasSide.Top,
                    "bottom" => CanvasSide.Bottom,
                    _ => CanvasSide.None,
                };
            }

            double? scale = p.TryGetDouble("scale", out var value) ? value : null;
            var step = p.TryGetString("step", out var stepName) ? stepName : null;
            var reset = (p.TryGetBool("reset", out var resetFlag) && resetFlag) || step == "reset";
            if (p.Failed)
            {
                return;
            }

            if (side == CanvasSide.None)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "side is left, right, top or bottom");
                return;
            }

            if (step is not (null or "smaller" or "larger" or "reset"))
            {
                reply.Error(IpcErrorCodes.InvalidParams, "step is smaller, larger or reset");
                return;
            }

            if (scale is { } asked && !double.IsFinite(asked))
            {
                reply.Error(IpcErrorCodes.InvalidParams, "scale is a number");
                return;
            }

            if (reset || scale is not null || step is not null)
            {
                AdjustShelf(side, scale, step == "smaller" ? -1 : step == "larger" ? 1 : 0, reset);
            }
            else
            {
                ReportShelves();
            }

            WriteShelves(reply.Result);
        });
        _report.AddLine(methods, "tinycomp/shelf", "shelf {step:smaller|larger|reset}");
        _report.AddLine(methods, "tinycomp/shelf", "shelf {side:left|right|top|bottom} {step:smaller|larger|reset}");
        _report.AddLine(methods, "tinycomp/shelf", "shelf {scale:number}");
        _report.AddLine(methods, "tinycomp/shelf", "shelf {side:left|right|top|bottom} {scale:number}");

        _report.Register(methods, "tinycomp/canvas-mode", "canvas mode [{mode:warp|scale|terrace}]", (ref IpcParams p, IpcReply reply) =>
        {
            CanvasWindowMode? mode = null;
            if (p.TryGetString("mode", out var modeName))
            {
                mode = modeName switch
                {
                    "warp" => CanvasWindowMode.Warp,
                    "scale" => CanvasWindowMode.Scale,
                    "terrace" => CanvasWindowMode.Terrace,
                    _ => null,
                };
                if (mode is null)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "mode is warp, scale or terrace");
                    return;
                }
            }

            if (p.Failed)
            {
                return;
            }

            if (SetCanvasMode(mode) is { } inForce)
            {
                var json = reply.Result;
                json.WriteStartObject();
                json.WriteString("mode", CanvasSetting.NameOf(inForce));
                json.WriteEndObject();
            }
        });

        _report.Register(methods, "tinycomp/ws", "ws", PrintWorkspaces);

        _report.Register(methods, "tinycomp/ws-step", "ws {direction:next|prev}", (ref IpcParams p, IpcReply _) =>
        {
            var direction = p.GetString("direction");
            if (!p.Failed)
            {
                SwitchWorkspace(direction == "next" ? 1 : -1);
            }
        });

        _report.Register(methods, "tinycomp/ws-create", "ws create [{name}]", (ref IpcParams p, IpcReply _) =>
        {
            string? name = p.TryGetString("name", out var named) ? named : null;
            if (!p.Failed && ViewAtCursor() is { } view)
            {
                ActivateWorkspace(view, CreateWorkspace(view, name, afterActive: true));
            }
        });

        _report.Register(methods, "tinycomp/ws-move", "ws move", () => CarryFocusedWindow(1));

        _report.Register(methods, "tinycomp/split", "split {fraction:number}", (ref IpcParams p, IpcReply _) =>
        {
            var fraction = p.GetDouble("fraction");
            if (!p.Failed)
            {
                SetSplit(fraction);
            }
        });

        _report.Register(methods, "tinycomp/dumpscene", "dumpscene", () => DumpTree(_scene.Root, 0));

        _report.Register(methods, "tinycomp/stats", "stats", ReportStats);

        _report.Register(methods, "tinycomp/nightlight", "nightlight {kelvin}", (ref IpcParams p, IpcReply reply) =>
        {
            var kelvin = p.GetString("kelvin");
            if (p.Failed)
            {
                return;
            }

            if (kelvin == "off")
            {
                ApplyNightLight(null);
                _report.Line($"NIGHTLIGHT off");
                return;
            }

            if (!double.TryParse(kelvin, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'kelvin' is off or a temperature, not '{kelvin}'");
                return;
            }

            ApplyNightLight(value);
            _report.Line($"NIGHTLIGHT {kelvin}K");
        });

        _report.Register(methods, "tinycomp/gc", "gc", () =>
        {
            var now = GC.GetAllocatedBytesForCurrentThread();
            _report.Line($"GC {now - _gcMark} bytes since last mark");
            _gcMark = now;
        });

        _report.Register(methods, "tinycomp/reload", "reload", Reload);

        _report.Register(methods, "tinycomp/bell", "bell", RingBell);

        _report.Register(methods, "tinycomp/xminimize", "xminimize {index:int} {minimized:bool}", (ref IpcParams p, IpcReply reply) =>
        {
            var index = (int)p.GetInt("index");
            var minimized = p.GetBool("minimized");
            if (p.Failed)
            {
                return;
            }

            if (index < 0 || index >= _xwindows.Count)
            {
                reply.Error(IpcErrorCodes.NotFound, $"no X window {index}");
                return;
            }

            SetMinimized(_xwindows[index], minimized);
        });

        _report.Register(methods, "tinycomp/mark", "mark {action:undo|clear}", (ref IpcParams p, IpcReply _) =>
        {
            var action = p.GetString("action");
            if (p.Failed)
            {
                return;
            }

            if (action == "undo")
            {
                _feedback?.UndoMark();
            }
            else
            {
                _feedback?.ClearMarks();
            }

            ScheduleEffectRepaint();
        });

        _report.Register(methods, "tinycomp/zoom", "zoom {direction:in|out|reset}", (ref IpcParams p, IpcReply _) =>
        {
            var direction = p.GetString("direction");
            if (p.Failed)
            {
                return;
            }

            switch (direction)
            {
                case "in":
                    _post.Zoom?.ZoomIn();
                    _post.Magnifier?.ZoomIn();
                    break;
                case "out":
                    _post.Zoom?.ZoomOut();
                    _post.Magnifier?.ZoomOut();
                    break;
                default:
                    _post.Zoom?.Reset();
                    _post.Magnifier?.Reset();
                    break;
            }

            ScheduleEffectRepaint();
        });
    }

    private bool View(int index, IpcReply reply, out Basin.Host.OutputView view)
    {
        if (index >= 0 && index < Views.Count)
        {
            view = Views[index];
            return true;
        }

        view = null!;
        reply.Error(IpcErrorCodes.NotFound, $"no output {index}");
        return false;
    }

    private void ReportStats()
    {
        _report.Line($"STATS transactions={_useTransactions} timedout={Transaction.TimedOutCount}");
        _report.Line($"STATS cursor theme={(_cursor.Images?.HasTheme == true ? _cursor.Images.Size.ToString() : "none")} " + $"showing={_cursor.Showing} " + $"on={(_cursor.CursorOutput?.Name ?? "none")}");
        foreach (var view in Views)
        {
            var so = view.Scene;
            _report.Line(so is null
                ? $"STATS {view.Output.Name} full-repaint scale={view.Output.Scale}"
                : $"STATS {view.Output.Name} scanout={so.ScanoutCommits} composed={so.ComposedCommits} skipped={so.SkippedCommits} direct={so.IsDirectScanout} offload={so.OffloadedLayers}/{so.OffloadCommits} swcursor={_cursor.IsSoftwareOn(view.Output)} scale={view.Output.Scale}");
            if (so is not null)
            {
                foreach (var reason in Enum.GetValues<PlaneDeclineReason>())
                {
                    if (so.DeclinedFor(reason) > 0)
                    {
                        _report.Line($"STATS   declined {reason} {so.DeclinedFor(reason)}");
                    }
                }
            }
        }
    }

    private sealed class SyntheticInput(TinyComp comp) : ISyntheticInput
    {
        public bool PointerMotionAbsolute(uint timeMs, double x, double y)
        {
            comp.MoveCursor(x, y, timeMs);
            return true;
        }

        public bool PointerButton(uint timeMs, uint button, bool pressed)
        {
            comp.OnButton(timeMs, button, pressed);
            return true;
        }

        public bool PointerAxis(uint timeMs, uint axis, double value, uint source)
        {
            comp.HandleAxis(timeMs, new PointerAxis((WlPointer.Axis)axis, value, Source: (WlPointer.AxisSource)source));
            return true;
        }

        public bool Key(uint timeMs, uint keycode, bool pressed)
        {
            comp.HandleKey(timeMs, keycode, pressed);
            return true;
        }

        public bool TryPointerPosition(out double x, out double y)
        {
            x = comp._cursorX;
            y = comp._cursorY;
            return true;
        }
    }
}
