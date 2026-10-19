using Basin.Ipc;

namespace SdlComp;

internal sealed partial class SdlComp
{
    private readonly IpcLineReport _report = new();
    private IpcPendingReply? _shotReply;

    private void StartIpc()
    {
        if (!_options.Ipc.Listen || !Basin.PlatformFacts.HasDescriptors)
        {
            return;
        }

        _ipc = _options.Ipc.Attach(_host.Loop, _host.Services, _host.Socket, new IpcSessionInfo
        {
            Compositor = "sdl-comp",
            Backend = "sdl",
            Renderer = _device.Name,
            Quit = _loop.RequestQuit,
        });
        RegisterCommands(_ipc.Methods);
        _ipc.Start();
        _ipc.StartLineFront();
    }

    private void RegisterCommands(IpcMethodRegistry methods)
    {
        _report.AddSessionLines(methods);
        _report.AddWindowLines(methods);
        _report.Register(methods, "sdlcomp/outputs", "outputs", (ref IpcParams p, IpcReply reply) =>
        {
            foreach (var window in OutputWindows)
            {
                var output = window.View.Output;
                var mode = output.CurrentMode;
                var box = _host.Layout.BoxOf(output);
                _report.Line(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"OUTPUT {output.Name} {mode.Width}x{mode.Height} scale={output.Scale:0.###} refresh={mode.RefreshMilliHz} at={box.X},{box.Y} hdr={(window.Hdr.Enabled ? $"{window.Hdr.SdrWhiteNits:0}/{window.Hdr.PeakNits:0}" : "off")}"));
            }
        });
        _report.Register(methods, "sdlcomp/shot", "shot {path}", (ref IpcParams p, IpcReply reply) =>
        {
            var path = p.GetString("path");
            if (p.Failed)
            {
                return;
            }

            if (_shotReply is { } superseded)
            {
                _shotReply = null;
                _ = IpcLineReport.Complete(superseded);
            }

            _shotReply = reply.Defer();
            _host.RequestScreenshot(path, written =>
            {
                if (_shotReply is not { } pending)
                {
                    return;
                }

                _shotReply = null;
                _ = written ? IpcLineReport.Complete(pending, $"SHOT {path}") : IpcLineReport.Complete(pending);
            });
            foreach (var window in OutputWindows)
            {
                window.View.RequestRender?.Invoke();
            }

            _loop.MarkActivity();
        });
    }
}
