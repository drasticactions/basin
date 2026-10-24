using Basin.Greetd.Ipc;
using Basin.Host;
using Basin.Ipc;

namespace Basin.Greetd.Host;

/// <content>
/// The control socket of the greeter host.
/// </content>
public sealed partial class GreeterHost
{
    private readonly IpcLineReport _report = new();
    private IpcServer? _ipc;
    private FrameSettle? _settle;

    /// <summary>
    /// Gets the line report that prints the output of the control methods.
    /// </summary>
    public IpcLineReport Report => _report;

    /// <summary>
    /// Gets the control socket server, or <see langword="null"/> before <see cref="Run"/>.
    /// </summary>
    public IpcServer? Ipc => _ipc;

    /// <summary>
    /// Gets or sets the program action for SIGHUP and for the <c>reload</c> method.
    /// </summary>
    public Action? Reload { get; set; }

    /// <summary>
    /// Gets or sets the program action for the <c>submit</c> method, the same as the program's own submit button.
    /// </summary>
    public Action? Submit { get; set; }

    /// <summary>
    /// Gets or sets the program action for the <c>clock</c> method. It receives the text to show as the time.
    /// </summary>
    public Action<string>? ClockOverride { get; set; }

    /// <summary>
    /// Gets or sets the program action for the <c>where</c> method.
    /// </summary>
    public Action? Where { get; set; }

    /// <summary>
    /// Gets or sets an action that registers the program's own methods after the host's methods.
    /// </summary>
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
        if (_access is { } access)
        {
            _ipc.GuardWithCurtain(access.Curtain);
        }

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
