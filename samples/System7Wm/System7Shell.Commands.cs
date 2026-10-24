using Basin.Cli;
using Basin.Diagnostics;
using Basin.Ipc;
using Basin.Host;
using Basin.Scene;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;
    private string? _shotPath;
    private IpcPendingReply? _shotReply;

    private FrameSettle? _settle;

    private void WireIpc()
    {
        _settle = new FrameSettle(_outputs, _uiDriver);
        if (_inspector is { } inspectorSession)
        {
            inspectorSession.Settle = _settle;
        }

        _ipc = _options.Ipc.Attach(_host.Loop, _services, _host.Socket, new IpcSessionInfo
        {
            Compositor = "system7-wm",
            Backend = _options.Backend.ToString().ToLowerInvariant(),
            Renderer = _options.Renderer,
            XwaylandDisplay = () => _xServer?.DisplayName,
            Quit = Stop,
            Settle = _settle,
            Processes = _processes,
        });
        _ipc.SyntheticInput = _seat.Injector;
        if (_access is { } access)
        {
            _ipc.GuardWithCurtain(access.Curtain);
        }
        RegisterCommands(_ipc.Methods);
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

    private void RegisterCommands(IpcMethodRegistry methods)
    {
        _report.AddInputLines(methods);
        _report.AddSessionLines(methods);
        RegisterInspectorCommands(methods);

        _report.Register(methods, "system7wm/shot", "shot {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                WriteScreenshot(path);
                _report.Line($"SHOT {path}");
            }
        });

        _report.Register(methods, "system7wm/shotraw", "shotraw {path}", (ref IpcParams p, IpcReply reply) =>
        {
            var path = p.GetString("path");
            if (p.Failed)
            {
                return;
            }

            _uiDriver.Pump();
            _shotPath = path;
            _shotReply = reply.Defer();
            _outputs.ScheduleAll();
            if (MainView is { } view)
            {
                _outputs.RepaintNow(view);
            }
        });

        _report.Register(methods, "system7wm/where", "where", PrintState);

        _report.Register(methods, "system7wm/depth", "depth {depth}", (ref IpcParams p, IpcReply _) =>
        {
            if (!p.TryGetRaw("depth", out var raw))
            {
                p.GetString("depth");
                return;
            }

            var name = System.Text.Encoding.UTF8.GetString(raw).Trim('"');
            if (ColorDepths.Parse(name) is not { } depth)
            {
                _report.Line($"DEPTH unknown {name}");
                return;
            }

            _config.Depth = depth;
            ApplyConfig();
            _report.Line($"DEPTH {ColorDepths.NameOf(depth)}");
        });

        RegisterShellCommands(methods);
    }

    private void TakeShot(OutputView driver)
    {
        if (_shotPath is not { } path || !ReferenceEquals(MainView, driver))
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

    private void PrintState()
    {
        _report.Line($"POINTER {_seat.PointerX:0.##} {_seat.PointerY:0.##}");
        foreach (var view in _outputs.Views)
        {
            var box = _layout.BoxOf(view.Output);
            var main = ReferenceEquals(view, MainView) ? " main" : "";
            _report.Line($"OUTPUT {view.Output.Name} {box.X},{box.Y},{box.Width}x{box.Height} scale={view.Output.Scale:0.###} chrome={DevicePlacement.ChromeScale(view.Output.Scale)}{main}");
        }

        if (MainView is { } mainView)
        {
            var area = WorkArea(mainView);
            _report.Line($"WORKAREA {area.X},{area.Y},{area.Width}x{area.Height}");
        }

        _report.Line($"LOCK {(_locked ? "on" : "off")}");
        _report.Line($"DEPTH {ColorDepths.NameOf(_config.Depth)}");
        _report.Line($"CURSOR {_cursor.Showing} drawn={_cursor.DrawnBy}");
        PrintShellState();
        _report.Line($"SURFACES {_chrome.Count}");
        _report.Line($"POPUPS {_uiDriver.Popups.Count}");
    }
}
