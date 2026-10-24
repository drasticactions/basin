using System.Runtime.InteropServices;
using Basin;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Hosted;
using Basin.Ipc;
using Basin.Scene;
using Basin.Shell.Nested;

namespace SdlComp;

internal sealed class HeadlessComp : IDisposable
{
    private readonly SdlCompOptions _options;
    private readonly BasinLogger _log;
    private readonly BasinHeadlessDriver _driver;
    private readonly ManualResetEventSlim _quit = new();
    private readonly IpcLineReport _report = new();
    private SceneCapturePack? _capture;
    private BasinViewOutput? _view;
    private NestedShell? _shell;
    private IpcServer? _ipc;
    private Action<FrameTick>? _tick;
    private bool _disposed;

    public HeadlessComp(SdlCompOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;
        _driver = new BasinHeadlessDriver(Create) { RenderNode = options.RenderNode };
        _driver.FrameCompleted += OnFrame;
    }

    public long Frames => _driver.Frames;

    public int Run()
    {
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
        _driver.Start();
        var (socket, dmabuf) = _driver.InvokeAsync(() => (_driver.Host.Socket, _driver.Host.Dmabuf is not null)).GetAwaiter().GetResult();
        BasinReport.Line(CompositorLines.Socket(socket));
        BasinReport.Line($"HEADLESS gpu={(_driver.RendersOnGpu ? "on" : "off")} dmabuf={(dmabuf ? "on" : "off")}");
        if (_options.Frames > 0)
        {
            _driver.RequestFrame();
        }

        _quit.Wait();
        if (_options.Screenshot is { Length: > 0 } path)
        {
            WriteScreenshot(path);
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _driver.FrameCompleted -= OnFrame;
        _driver.Stop(() =>
        {
            _ipc?.Dispose();
            if (_tick is not null)
            {
                _driver.Host.Session.BeforeDispatch -= _tick;
            }

            _shell?.Dispose();
            _view?.Dispose();
        });
        _driver.Dispose();
        _quit.Dispose();
    }

    private BasinCompositorHost Create()
    {
        var options = SdlComp.HostOptions(_options.Socket);
        var configure = options.ConfigureServices;
        var host = new BasinCompositorHost(options with
        {
            ManagedTransport = false,
            ConfigureServices = (host, services) =>
            {
                configure?.Invoke(host, services);
                services.With(_capture = new SceneCapturePack(host.Scene, host.Layout));
            },
        });
        var scale = _options.Scale ?? 1.0;
        _view = host.CreateViewOutput(
            (int)Math.Round(_options.Width * scale), (int)Math.Round(_options.Height * scale), scale, NestedShell.OutputKey);
        var settings = new ShellSettings();
        _shell = new NestedShell(
            host, _view, settings, new PanelLayout(0, 0, 0), KeyTable.Build(settings.Keys, []), _driver.Post, [NestedTheme.Always]);
        _shell.AttachCapture(_capture!);
        _tick = _ => _shell.Tick(Environment.TickCount64);
        host.Session.BeforeDispatch += _tick;
        StartIpc(host);
        return host;
    }

    private void StartIpc(BasinCompositorHost host)
    {
        if (!_options.Ipc.Listen || !PlatformFacts.HasDescriptors)
        {
            return;
        }

        _ipc = _options.Ipc.Attach(host.Loop, host.Services, host.Socket, new IpcSessionInfo
        {
            Compositor = "sdl-comp",
            Backend = "headless",
            Renderer = _driver.RenderNode is null ? "skia" : "skia-gl",
            Quit = _quit.Set,
        });
        _report.AddSessionLines(_ipc.Methods);
        _report.AddWindowLines(_ipc.Methods);
        _report.Register(_ipc.Methods, "sdlcomp/shot", "shot {path}", (ref IpcParams p, IpcReply reply) =>
        {
            var path = p.GetString("path");
            if (p.Failed)
            {
                return;
            }

            var pending = reply.Defer();
            host.RequestScreenshot(path, written =>
                _ = written ? IpcLineReport.Complete(pending, $"SHOT {path}") : IpcLineReport.Complete(pending));
            _driver.RequestFrame();
        });
        _ipc.Start();
        _ipc.StartLineFront();
    }

    private void OnFrame()
    {
        if (_options.Frames > 0)
        {
            if (_driver.Frames >= _options.Frames)
            {
                _quit.Set();
            }
            else
            {
                _driver.RequestFrame();
            }
        }
    }

    private void WriteScreenshot(string path)
    {
        var written = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _driver.Post(() => _driver.Host.RequestScreenshot(path, written.SetResult));
        if (written.Task.Wait(TimeSpan.FromSeconds(10)) && written.Task.Result)
        {
            _log.Info($"screenshot written to {path}");
        }
        else
        {
            _log.Warn($"screenshot: {path} was not written");
        }
    }

    private void OnSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        _quit.Set();
    }
}
