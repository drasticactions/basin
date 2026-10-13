using Basin.Greetd.Ipc;
using Basin.Host;
using Basin.Ipc;

namespace Basin.Greetd.Host;

public sealed partial class GreeterHost
{
    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;
    private FrameSettle? _settle;

    public IpcLineReport Report => _report;

    public IpcServer? Ipc => _ipc;

    public Action? Reload { get; set; }

    public Action? Submit { get; set; }

    public Action<string>? ClockOverride { get; set; }

    public Action? Where { get; set; }

    public Action<IpcMethodRegistry>? MoreCommands { get; set; }

    private void WireCommands()
    {
        _settle = new FrameSettle(_outputs, _uiDriver);
        _ipc = new IpcServer(_host.Loop, _services, new IpcSessionInfo
        {
            Compositor = _options.Program,
            Backend = _options.Backend,
            Renderer = _rendererName,
            Quit = () => Stop("quit"),
            Settle = _settle,
            WaylandSocket = _host.Socket,
        }, _options.IpcPath, _options.IpcListen);
        _ipc.SyntheticInput = Seat.Injector;
        Register(_ipc.Methods);
        _ipc.Start();
        _ipc.StartLineFront();
    }

    private void Register(IpcMethodRegistry methods)
    {
        var ns = _options.CommandNamespace;
        _report.AddInputLines(methods);
        _report.AddSessionLines(methods);
        if (_flow is { } flow)
        {
            GreeterIpc.Register(methods, _report, flow);
        }

        _report.Register(methods, ns + "/type", "type {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed)
            {
                _uiDriver.Pump();
                Seat.Router.TextCommit(text);
                _uiDriver.Pump();
            }
        });

        _report.Register(methods, ns + "/submit", "submit", () =>
        {
            _uiDriver.Pump();
            Submit?.Invoke();
        });

        _report.Register(methods, ns + "/clock", "clock {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed)
            {
                ClockOverride?.Invoke(text);
            }
        });

        _report.Register(methods, ns + "/shot", "shot {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                WriteScreenshot(path);
                _report.Line($"SHOT {path}");
            }
        });

        _report.Register(methods, ns + "/shotraw", "shotraw {path}", (ref IpcParams p, IpcReply _) =>
        {
            var path = p.GetString("path");
            if (!p.Failed)
            {
                var buffer = _outputs.Views.FirstOrDefault()?.LastPresentedBuffer;
                _report.Line(ShotRawLine(Basin.Scene.SceneScreenshot.WritePresented(buffer, _renderer, path), buffer, path));
            }
        });

        _report.Register(methods, ns + "/where", "where", () => Where?.Invoke());

        _report.Register(methods, ns + "/reload", "reload", () => Reload?.Invoke());

        MoreCommands?.Invoke(methods);
    }

    private static string ShotRawLine(Basin.Scene.ScreenshotOutcome outcome, IBuffer? buffer, string path) => outcome switch
    {
        Basin.Scene.ScreenshotOutcome.NoFrame => "SHOTRAW none",
        Basin.Scene.ScreenshotOutcome.Unreadable => $"SHOTRAW unreadable {buffer!.Width}x{buffer.Height}",
        _ => $"SHOTRAW {path} {buffer!.Width}x{buffer.Height}",
    };
}
