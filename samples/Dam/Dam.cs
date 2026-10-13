using Basin;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Ipc;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Dam;

internal sealed partial class Dam : IDisposable
{
    private readonly DamOptions _options;
    private IpcServer? _ipc;
    private Basin.Host.FrameSettle? _settle;
    private readonly BasinLogger _log;

    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly Basin.Host.BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private Basin.Desktop.ColorCapabilityPack _colorPack = null!;
    private readonly BasinServices _services;
    private readonly OutputDriver _outputs;
    private readonly CompositorRunLoop _loop;
    private readonly DamSeat _damSeat;
    private readonly Basin.Seat.Backends.InputConfigurator _inputConfigurator = new();
    private readonly Basin.XWayland.XWaylandServer? _xServer;
    private PrimaryClient? _client;
    private readonly FrameLimiter? _limiter;
    private readonly Basin.Color.PanelTable _panels;

    public static int Run(DamOptions options, BasinLogger log, out long rendered)
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
            log.Error($"{error.Message}");
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

        BasinReport.Line(CompositorLines.Frames(rendered));
        if (CompositorLines.Census() is { } census)
        {
            log.Error($"{census}");
        }

        return status;
    }

    internal Dam(DamOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;

        var stack = CreateStack(options.Renderer, log);
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;

        _host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()));

        _colorPack = new Basin.Desktop.ColorCapabilityPack(_layout, _renderer);
        var servicePack = new DesktopServicePack(_scene, _layout, _renderer, _host.Drm);
        var capturePack = servicePack.Capture;
        var cursorTheme = servicePack.CursorTheme;
        var inputSink = new Basin.Seat.Backends.HookInputSink();
        _services = _host.CreateServices()
            .Use(_layout)
            .With(servicePack)
            .With(_colorPack)
            .Use<Basin.Capabilities.IInputSink>(inputSink);
        _services.Install(KioskPack.Default.Without("org_kde_kwin_server_decoration_manager"));
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
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath, captureFormats: _renderer.DmabufRenderFormats));
        }

        _services.UseDefault<Basin.Capabilities.IInputDeviceConfiguration>(_inputConfigurator);
        if (options.FrameLimit > 0)
        {
            _limiter = new FrameLimiter(_host.Loop, _layout);
            _services.Use<Basin.Capabilities.IFrameLimits>(_limiter);
        }

        _panels = Basin.Color.PanelTable.Load(log, options.Panels);
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
            ConfiguredScale = static _ => 1,
            VrrPacing = options.VrrPacing,
            ContentTypeOverride = options.GameContent ? OutputContentType.Game : null,
        };
        _outputs.StampModeset += OnStampModeset;
        _loop = new CompositorRunLoop(_host, _outputs);
        _outputs.Emptied += Stop;
        _outputs.ModesetRefused += card =>
            log.Error($"modeset refused by {card.Name} in every mode");
        _outputs.Added += view => BasinReport.Line($"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        _outputs.Removed += view =>
        {
            if (view.Output is Basin.Backend.Drm.DrmOutput card)
            {
                BasinReport.Line($"OUTPUT - {card.Name}");
            }
        };
        _outputs.ModeChanged += view => BasinReport.Line($"MODE {view.Output.Name} {view.Width}x{view.Height}");
        _outputs.ScanoutChanged += (view, choice) =>
            BasinReport.Line(CompositorLines.Scanout(view.Output.Name, choice, view.SwapModifiers.Length));
        Views = new DamViews(_scene, _layout, Seat, _outputs)
        {
            Scaler = options.Scaler,
            Filter = options.Filter,
            FrameLimit = options.FrameLimit,
            Limits = _limiter,
        };
        Views.Attach(Shell, _services.Require<XdgToplevelSource>());
        if (xwayland is not null)
        {
            _xServer = xwayland.Native!.Server;
            xwayland.Native.WindowManagerReady += Views.AttachXWayland;
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
        _damSeat.AttachConfigurator(_inputConfigurator);

        _outputs.CreateInitialOutputs();
        WireColor();
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

    internal void Stop() => _loop.Stop();

    private int RunLoop()
    {
        BasinReport.Line(CompositorLines.Socket(_host.Socket));
        _settle = new Basin.Host.FrameSettle(_outputs);
        _ipc = _options.Ipc.Attach(_host.Loop, _services, _host.Socket, new IpcSessionInfo
        {
            Compositor = "dam",
            Backend = _options.Backend.ToString().ToLowerInvariant(),
            Renderer = _options.Renderer,
            XwaylandDisplay = () => _xServer?.DisplayName,
            Quit = Stop,
            Settle = _settle,
        });
        _ipc.SyntheticInput = _damSeat.Injector;
        var lines = new IpcLineReport();
        lines.AddInputLines(_ipc.Methods);
        lines.AddSessionLines(_ipc.Methods);
        _ipc.Start();
        _ipc.StartLineFront();

        if (_options.Application.Length > 0)
        {
            _client = PrimaryClient.Spawn(
                _options.Application, _host.Socket, _xServer?.DisplayName, _host.Loop, Stop, _log, _options.Subreaper);
            if (_client is null)
            {
                return 1;
            }
        }

        _damSeat.CenterCursor();

        _loop.Frames = _options.Frames;
        _loop.Run();
        return 0;
    }

    private static RenderStack CreateStack(string rendererName, BasinLogger log)
    {
        var name = rendererName;
        return RendererCatalog.CreateWithFallback(
            ref name,
            RendererCatalog.FindRenderNode(),
            fallback => log.Warn($"{(fallback.Describe())}"));
    }

    public void Dispose()
    {
        _host.DisconnectClients();
        _ipc?.Dispose();
        _settle?.Dispose();
        _ipc = null;
        _colorPack?.Luts.Dispose();
        _client?.Dispose();
        _outputs.Dispose();
        _limiter?.Dispose();
        _scene.Root.Destroy();
        _inputConfigurator.Dispose();
        _damSeat.Dispose();
        _services.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
