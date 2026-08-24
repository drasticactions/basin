using Basin;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Xdg;
using Microsoft.Extensions.Logging;
using Wayland.Server;

namespace Dam;

internal sealed class Dam : IDisposable
{
    private readonly DamOptions _options;
    private readonly ILogger _log;

    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly Basin.Host.BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private readonly BasinServices _services;
    private readonly OutputDriver _outputs;
    private readonly DamSeat _damSeat;
    private readonly Basin.XWayland.XWaylandServer? _xServer;
    private PrimaryClient? _client;

    private bool _running = true;

    public static int Run(DamOptions options, ILogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        if (!PrimaryClient.DropPermissions(log))
        {
            return 1;
        }

        int status;
        PrimaryClient? client;
        try
        {
            using var dam = new Dam(options, log);
            status = dam.RunLoop();
            rendered = dam._outputs.PrimaryRendered;
            client = dam._client;
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException)
        {
            log.LogError("{Reason}", error.Message);
            return 1;
        }

        if (client is not null)
        {
            var appStatus = client.WaitAndDecode();
            if (status == 0 && client.ReturnAppCode)
            {
                status = appStatus;
            }
        }

        Console.WriteLine(
            $"FRAMES {rendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}");
        if (BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0))
        {
            BasinCounters.WriteCensus(Console.Error);
        }

        return status;
    }

    internal Dam(DamOptions options, ILogger log)
    {
        _options = options;
        _log = log;

        var stack = CreateStack(options.Renderer, log);
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;

        _host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()));

        var capturePack = new SceneCapturePack(_scene, _layout);
        capturePack.Capture.Renderer = _renderer;
        var cursorTheme = new Basin.Capabilities.Defaults.CursorImageTheme();
        var inputSink = new Basin.Seat.Backends.HookInputSink();
        _services = _host.CreateServices()
            .Use(_layout)
            .With(capturePack)
            .With(new DrmCapabilityPack(_renderer, _host.Drm))
            .Use<Basin.Capabilities.ICursorTheme>(cursorTheme)
            .Use<Basin.Capabilities.IInputSink>(inputSink);
        var pack = KioskPack.Default.Without("org_kde_kwin_server_decoration_manager");
        if (_host.Drm is null)
        {
            pack = pack.Without("wp_drm_lease_device_v1");
        }

        _services.Install(pack);
        _services.Install(new KdeServerDecorationModule(options.ServerDecorations
            ? Basin.Desktop.KdeServerDecorationManager.DecorationMode.Server
            : Basin.Desktop.KdeServerDecorationManager.DecorationMode.Client));
        Basin.XWayland.XWaylandModule? xwayland = null;
        if (options.EnableXWayland)
        {
            xwayland = new Basin.XWayland.XWaylandModule { IncludeKeyboardGrab = false };
            _services.Install(xwayland);
        }

        if (_renderer.Device is { } renderDevice)
        {
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath));
        }

        _services.Freeze();

        Seat = _services.Require<Basin.Seat.Seat>();
        Shell = _services.Require<XdgShell>();
        Decorations = _services.Require<XdgDecorationManager>();
        Decorations.DefaultMode = options.ServerDecorations
            ? DecorationMode.ServerSide
            : DecorationMode.ClientSide;
        Decorations.ChooseMode = (_, _) => options.ServerDecorations
            ? DecorationMode.ServerSide
            : DecorationMode.ClientSide;

        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = capturePack,
            Frames = _services.Require<Basin.Capabilities.IFrameClock>(),
            ContinuousRepaint = options.Frames > 0,
            LastOnly = options.LastOutputOnly,
            NestedName = _ => "dam",
        };
        _outputs.Emptied += Stop;
        _outputs.ModesetRefused += card =>
            log.LogError("modeset refused by {Output} in every mode", card.Name);
        _outputs.Added += view => Console.WriteLine(
            $"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        _outputs.Removed += view =>
        {
            if (view.Output is Basin.Backend.Drm.DrmOutput card)
            {
                Console.WriteLine($"OUTPUT - {card.Name}");
            }
        };
        _outputs.ModeChanged += view => Console.WriteLine(
            $"MODE {view.Output.Name} {view.Width}x{view.Height}");
        _outputs.ScanoutChanged += (view, choice) => Console.WriteLine(choice switch
        {
            ScanoutChoice.DeviceBuffers =>
                $"SCANOUT {view.Output.Name} device modifiers={view.SwapModifiers.Length}",
            ScanoutChoice.DumbLinear =>
                $"SCANOUT {view.Output.Name} dumb linear; every frame reads the framebuffer back",
            _ =>
                $"SCANOUT {view.Output.Name} device buffers refused by the plane; falling back to dumb linear",
        });
        Views = new DamViews(_scene, _layout, Seat, _outputs);
        Views.Attach(Shell, _services.Require<XdgToplevelSource>());
        if (xwayland is not null)
        {
            _xServer = xwayland.Server;
            xwayland.WindowManagerReady += Views.AttachXWayland;
        }
        _outputs.LayoutChanged += Views.PositionAll;
        if (_services.Find<Basin.Capabilities.IOutputConfiguration>() is { } outputConfiguration)
        {
            outputConfiguration.Applied += _ => Views.PositionAll();
        }

        _damSeat = new DamSeat(
            _host,
            _services,
            Views,
            _outputs,
            _scene,
            _layout,
            cursorTheme,
            inputSink,
            (_services.Find<Basin.Capabilities.IIdleSource>() as Basin.Seat.SeatIdleSource)!,
            options.AllowVtSwitch,
            Stop,
            log);

        _outputs.CreateInitialOutputs();
        if (_options.Backend == BackendKind.Drm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("no connected output");
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += Stop;
        }
    }

    internal Basin.Seat.Seat Seat { get; }

    internal XdgShell Shell { get; }

    internal XdgDecorationManager Decorations { get; }

    internal DamViews Views { get; }

    internal Basin.Host.BasinHost Host => _host;

    internal OutputDriver Outputs => _outputs;

    internal DamSeat DamSeat => _damSeat;

    internal OutputLayout Layout => _layout;

    internal void Stop() => _running = false;

    private int RunLoop()
    {
        Console.WriteLine($"SOCKET {_host.Socket}");
        Console.Out.Flush();

        var interrupt = _host.Loop.AddSignal(Signal.Interrupt, _ => Stop());
        var terminate = _host.Loop.AddSignal(Signal.Terminate, _ => Stop());

        if (_options.Application.Length > 0)
        {
            _client = PrimaryClient.Spawn(
                _options.Application, _host.Socket, _xServer?.DisplayName, _host.Loop, Stop, _log);
            if (_client is null)
            {
                interrupt.Remove();
                terminate.Remove();
                return 1;
            }
        }

        _damSeat.CenterCursor();

        var frames = _options.Frames;
        while (_running && (frames == 0 || _outputs.PrimaryRendered < frames))
        {
            _host.Loop.Dispatch(16);
            _host.Parent?.Flush();
        }

        interrupt.Remove();
        terminate.Remove();
        return 0;
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
        _client?.Dispose();
        _outputs.Dispose();
        _scene.Root.Destroy();
        _damSeat.Dispose();
        _services.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
