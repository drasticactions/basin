using Basin;
using Basin.Cli;
using Basin.Desktop;
using Basin.Host;
using Basin.Diagnostics;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Xdg;
using Microsoft.Extensions.Logging;
using Wayland.Server;

namespace EightWm;

internal sealed partial class Shell : IDisposable
{
    private readonly ShellOptions _options;
    private readonly ILogger _log;

    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly Basin.Host.BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private readonly BasinServices _services;
    private readonly SceneCapturePack _capture;
    private FifoManager? _fifo;
    private readonly OutputDriver _outputs;
    private readonly List<ShellView> _shellViews = [];
    private string? _shotPath;
    private int _shotView;
    private readonly ShellSeat _seat;
    private readonly List<AppWindow> _apps = [];
    private readonly Dictionary<Surface, AppWindow> _owners = [];
    private Basin.Desktop.PopupPlacer _popups = null!;

    private readonly Dictionary<AppWindow, ShellView> _homes = [];

    private Basin.XWayland.XWaylandServer? _xServer;

    private bool _running = true;

    public static int Run(ShellOptions options, ILogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var shell = new Shell(options, log);
            status = shell.RunLoop();
            rendered = shell._outputs.PrimaryRendered;
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException)
        {
            log.LogError("{Reason}", error.Message);
            return 1;
        }

        Console.WriteLine(
            $"FRAMES {rendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}");
        if (BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0))
        {
            BasinCounters.WriteCensus(Console.Error);
        }

        return status;
    }

    internal Shell(ShellOptions options, ILogger log)
    {
        _options = options;
        _log = log;

        var stack = CreateStack(options.Renderer, log);
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;

        _host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()) with
            {
                SocketFd = options.SocketFd,
            });

        _popups = new Basin.Desktop.PopupPlacer(_layout);
        _capture = new SceneCapturePack(_scene, _layout);
        _capture.Capture.Renderer = _renderer;
        var cursorTheme = new Basin.Capabilities.Defaults.CursorImageTheme();
        var inputSink = new Basin.Seat.Backends.HookInputSink();
        _services = _host.CreateServices()
            .Use(_layout)
            .With(_capture)
            .With(new DrmCapabilityPack(_renderer, _host.Drm))
            .Use<Basin.Capabilities.ICursorTheme>(cursorTheme)
            .Use<Basin.Capabilities.IColorProfileService>(new Basin.Color.Lcms2ColorProfileService())
            .Use<Basin.Capabilities.IInputSink>(inputSink)
            .Use<Basin.Capabilities.IActivationTokens>(new Basin.Capabilities.Defaults.DefaultActivationTokens())
            .Use<Basin.Capabilities.IBell>(Basin.Capabilities.Defaults.SilentBell.Instance);

        var pack = DesktopPack.For("eight-wm");
        if (_host.Drm is null)
        {
            pack = pack.Without("wp_drm_lease_device_v1");
        }

        _services.Install(pack);
        Basin.XWayland.XWaylandModule? xwayland = null;
        if (OperatingSystem.IsLinux() && _options.XWayland)
        {
            xwayland = new Basin.XWayland.XWaylandModule();
            _services.Install(xwayland);
        }

        if (_renderer.Device is { } renderDevice)
        {
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath));
        }

        _services.Freeze();

        Seat = _services.Require<Basin.Seat.Seat>();
        Shells = _services.Require<XdgShell>();
        var toplevels = _services.Require<Basin.Capabilities.IToplevelModel>();
        _capture.Attach(toplevels, surface =>
            _owners.TryGetValue(surface, out var app)
                ? new ToplevelCaptureTrees(app.Slot, null)
                : null);
        var decorations = _services.Require<XdgDecorationManager>();
        decorations.DefaultMode = DecorationMode.ServerSide;
        decorations.ChooseMode = (_, _) => DecorationMode.ServerSide;

        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = _capture,
            Frames = _services.Require<Basin.Capabilities.IFrameClock>(),
            Requested = options.Outputs,
            Scales = options.Scales,
            ContinuousRepaint = options.Frames > 0,
            NestedName = index => $"eight-wm-{index}",
            HeadlessMode = new OutputMode(1366, 768, 60_000),
        };
        _outputs.Added += driver =>
        {
            var view = new ShellView(driver, _scene);
            driver.Tag = view;
            _shellViews.Add(view);
            if (driver.Output is Basin.Backend.Wayland.WaylandOutput nested)
            {
                nested.SetTitle("eight-wm");
            }

            Console.WriteLine($"OUTPUT {driver.Output.Name} {driver.Output.CurrentMode.Width}x{driver.Output.CurrentMode.Height}");
        };
        _outputs.Removed += driver =>
        {
            if (driver.Output is Basin.Backend.Drm.DrmOutput card)
            {
                Console.WriteLine($"OUTPUT - {card.Name}");
            }

            var view = ViewOf(driver);
            _shellViews.Remove(view);
            view.Destroy();
        };
        _outputs.Emptied += Stop;
        _outputs.ModesetRefused += card =>
            log.LogError("modeset refused by {Output} in every mode", card.Name);
        _outputs.ModeChanged += driver => Console.WriteLine(
            $"MODE {driver.Output.Name} {driver.Width}x{driver.Height}");
        _outputs.ScanoutChanged += (driver, choice) => Console.WriteLine(choice switch
        {
            ScanoutChoice.DeviceBuffers =>
                $"SCANOUT {driver.Output.Name} device modifiers={driver.SwapModifiers.Length}",
            ScanoutChoice.DumbLinear =>
                $"SCANOUT {driver.Output.Name} dumb linear; every frame reads the framebuffer back",
            _ =>
                $"SCANOUT {driver.Output.Name} device buffers refused by the plane; falling back to dumb linear",
        });
        _outputs.Painted += TakeShot;
        _outputs.LayoutChanged += () =>
        {
            foreach (var view in _shellViews)
            {
                view.Reposition();
            }

            RearrangeLayers();
            RelayoutAll();
        };
        _outputs.BeforeRepaint += driver => AdvanceAnimations(ViewOf(driver));
        _fifo = _services.Find<FifoManager>();

        _seat = new ShellSeat(_host, _services, this, _outputs, _scene, _layout, cursorTheme, inputSink, log);

        LoadConfig();
        AttachColor();
        AttachScales();
        AttachShell();
        AttachLayerShell();
        if (xwayland is not null)
        {
            _xServer = xwayland.Server;
            xwayland.WindowManagerReady += AttachXWayland;
        }

        _outputs.Added += driver => AttachStart(ViewOf(driver));
        _outputs.Added += driver => AttachCharms(ViewOf(driver));
        _outputs.Added += driver => AttachTitle(ViewOf(driver));
        _outputs.Added += driver => AttachSwitcher(ViewOf(driver));
        _outputs.CreateInitialOutputs();
        if (_options.Backend == BackendKind.Drm && Views.Count == 0)
        {
            throw new InvalidOperationException("no connected output");
        }

        if (Views.Count > 0)
        {
            var start = Math.Clamp(StartOutputNow, 0, Views.Count - 1);
            Views[start].StartVisible = true;
        }

        foreach (var view in Views)
        {
            view.Host.MinWidth = MinWidthNow;
            view.Host.MaxCells = _config.MaxCells;
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += Stop;
        }
    }

    internal Basin.Seat.Seat Seat { get; }

    internal XdgShell Shells { get; }

    internal Scene Scene => _scene;

    internal OutputLayout Layout => _layout;

    internal OutputDriver Outputs => _outputs;

    internal IReadOnlyList<ShellView> Views => _shellViews;

    internal static ShellView ViewOf(OutputView driver) => (ShellView)driver.Tag!;

    private void TakeShot(OutputView driver)
    {
        if (_shotPath is not { } path || _shotView < 0 || _shotView >= _outputs.Views.Count
            || !ReferenceEquals(_outputs.Views[_shotView], driver)
            || driver.LastPresentedBuffer is not { } shot)
        {
            return;
        }

        _shotPath = null;
        SceneScreenshot.WritePresented(shot, _renderer, path);
        Console.WriteLine($"SHOT {path}");
    }

    internal ShellOptions Options => _options;

    internal ShellSeat SeatInput => _seat;

    internal IReadOnlyList<AppWindow> Apps => _apps;

    internal Basin.Host.BasinHost Host => _host;

    internal void Stop() => _running = false;

    private int RunLoop()
    {
        if (_host.Socket.Length > 0)
        {
            Console.WriteLine($"SOCKET {_host.Socket}");
            Console.Out.Flush();
        }

        var interrupt = _host.Loop.AddSignal(Signal.Interrupt, _ => Stop());
        var terminate = _host.Loop.AddSignal(Signal.Terminate, _ => Stop());
        var hangup = _host.Loop.AddSignal(Signal.Hangup, _ => Reload());
        WireStdin();

        _seat.CenterCursor();

        if (_options.Client is { Length: > 0 } command)
        {
            Spawn(command);
        }

        var frames = _options.Frames;
        while (_running && (frames == 0 || _outputs.PrimaryRendered < frames))
        {
            _host.Loop.Dispatch(16);
            if (_fifo is { HasPendingBarriers: true })
            {
                foreach (var view in Views)
                {
                    view.Scheduler?.ScheduleRepaint();
                }
            }

            foreach (var view in Views)
            {
                if (view.Background.Enabled && view.Start is { Dirty: true })
                {
                    view.Scheduler?.ScheduleRepaint();
                }
            }

            _host.Parent?.Flush();
            ExpireCloseTimers();
            ExpireSplashes();
            PollTiles();
        }

        if (_options.Screenshot is { Length: > 0 } path && Views.Count > 0)
        {
            SettleAnimations();
            _shotPath = path;
            _shotView = 0;
            _outputs.RepaintNow(Views[0].Driver);
        }

        DisconnectClients();
        interrupt.Remove();
        terminate.Remove();
        hangup.Remove();
        UnwireStdin();
        return 0;
    }

    private void DisconnectClients()
    {
        var clients = new List<Wayland.Server.WlClient>();
        foreach (var app in _apps)
        {
            if (app.Surface is { IsDestroyed: false } surface &&
                !clients.Contains(surface.Resource.Client))
            {
                clients.Add(surface.Resource.Client);
            }
        }

        foreach (var client in clients)
        {
            if (!client.IsDestroyed)
            {
                client.Destroy();
            }
        }

        var drain = System.Diagnostics.Stopwatch.StartNew();
        while (_apps.Count > 0 && drain.ElapsedMilliseconds < 2000)
        {
            _host.Loop.Dispatch(20);
        }
    }

    internal void Spawn(string command)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(command);
            info.Environment["WAYLAND_DISPLAY"] = _host.Socket;
            if (_xServer?.DisplayName is { Length: > 0 } display)
            {
                info.Environment["DISPLAY"] = display;
            }

            System.Diagnostics.Process.Start(info);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.LogWarning("cannot start '{Command}': {Reason}", command, error.Message);
        }
    }

    private static RenderStack CreateStack(string rendererName, ILogger log)
    {
        const string renderNode = "/dev/dri/renderD128";
        var name = rendererName;
        return RendererCatalog.CreateWithFallback(
            ref name,
            File.Exists(renderNode) ? renderNode : null,
            fallback => Report(log, fallback));
    }

    private static void Report(ILogger log, RendererFallback fallback)
    {
        if (fallback.Reason is null)
        {
            log.LogWarning(
                "{Renderer} requested but no render node was found; using software rendering", fallback.From);
            return;
        }

        log.LogWarning(
            "{Renderer} renderer unavailable ({Reason}); falling back to {Fallback}",
            fallback.From, fallback.Reason, fallback.To);
    }

    public void Dispose()
    {
        ReleaseChrome();
        _seat.Dispose();
        _outputs.Dispose();
        _scene.Root.Destroy();
        _services.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
