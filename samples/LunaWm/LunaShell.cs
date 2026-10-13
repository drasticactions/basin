using Basin;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;

namespace LunaWm;

internal sealed partial class LunaShell : IDisposable
{
    private readonly LunaOptions _options;
    private readonly BasinLogger _log;
    private readonly IRenderer _renderer;
    private readonly string _rendererName;
    private readonly IAllocator? _deviceAllocator;
    private readonly BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private readonly BasinServices _services;
    private readonly SceneCapturePack _capture;
    private readonly OutputDriver _outputs;
    private readonly CompositorRunLoop _loop;
    private readonly ProcessTracker _processes;
    private readonly Basin.Desktop.ColorCapabilityPack _colorPack;
    private readonly Basin.Seat.Backends.InputConfigurator _inputConfigurator = new();
    private readonly List<LunaView> _views = [];
    private readonly SceneTree _sessionLockLayer;
    private readonly SceneTree _popupLayer;
    private readonly OutputScreens _screens;
    private readonly BasinGlGpu? _gpu;
    private readonly AvaloniaUIHost _ui;
    private readonly UISurfaceIndex _chromeIndex = new();
    private readonly UIDriver _uiDriver;
    private readonly LunaSeat _seat;
    private readonly MemoryBuffer _shadowTile;
    private readonly MemoryBuffer _black;
    private InitProcess? _initProcess;
    private LunaConfig _config;
    private Basin.XWayland.XWaylandServer? _xServer;

    public static int Run(LunaOptions options, LunaConfig config, BasinLogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var shell = new LunaShell(options, config, log);
            status = shell.RunLoop();
            rendered = shell._outputs.PrimaryRendered;
            shell.WriteScreenshot(options.Screenshot);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException)
        {
            log.Error($"{error.Message}");
            return 1;
        }

        BasinReport.Line(CompositorLines.Frames(rendered));
        if (CompositorLines.Census() is { } census)
        {
            log.Error($"{census}");
        }

        return status;
    }

    internal LunaShell(LunaOptions options, LunaConfig config, BasinLogger log)
    {
        _options = options;
        _log = log;
        _config = config;

        var name = options.Renderer;
        var stack = RendererCatalog.CreateWithFallback(ref name, RendererCatalog.FindRenderNode(), fallback => log.Warn($"{fallback.Describe()}"));
        _renderer = stack.Renderer;
        _rendererName = name;
        _deviceAllocator = stack.DeviceAllocator;
        BasinReport.Line($"RENDERER {name}");

        _host = BasinHost.Create(HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()) with
        {
            SocketFd = options.SocketFd,
            Handoff = options.Handoff.Resolve(config.Handoff.Mode),
        });
        _processes = new ProcessTracker(_host.Loop, () => ProcessTracker.SessionEnvironment(_host.Socket, _xServer?.DisplayName, _ipc?.Path, _xwaylandModule?.ChildEnvironment()))
        {
            X11Routes = density => _xwaylandModule?.Route(density),
            X11Default = launch => _config.Launch.ForArgv(launch.DesktopId, launch.Argv),
        };

        _colorPack = new Basin.Desktop.ColorCapabilityPack(_layout, _renderer);
        var servicePack = new DesktopServicePack(_scene, _layout, _renderer, _host.Drm);
        _capture = servicePack.Capture;
        var inputSink = new Basin.Seat.Backends.HookInputSink();
        _tokens = new LunaActivationTokens();
        servicePack.ActivationTokens = _tokens;
        _services = _host.CreateServices()
            .Use(_layout)
            .With(servicePack)
            .With(_colorPack)
            .Use<IInputSink>(inputSink)
            .Use<IGlobalShortcuts>(new Basin.Hypr.PreferredTriggerShortcuts())
            .Use<ILockState>(_lockState);
        var desktop = DesktopPack.For("luna-wm");
        InstallKeyboard(desktop);
        _services.Install(desktop);
        _services.Install(Basin.Hypr.HyprPack.Default.Without("hyprland_ctm_control_manager_v1"));

        Basin.XWayland.XWaylandModule? xwayland = null;
        if (OperatingSystem.IsLinux() && (options.XWayland ?? _config.XWayland))
        {
            xwayland = new Basin.XWayland.XWaylandModule { IncludeLowDensity = _config.XWaylandLowDensity };
            _xwaylandModule = xwayland;
            _services.Install(xwayland);
        }

        if (_renderer.Device is { } renderDevice)
        {
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath, captureFormats: _renderer.DmabufRenderFormats));
        }

        _services.UseDefault<IInputDeviceConfiguration>(_inputConfigurator);
        InstallAppMenu();
        _sessionLockLayer = new SceneTree(_scene.Root);
        CreateLayerDrivers();
        _services.Freeze();

        Seat = _services.Require<Basin.Seat.Seat>();
        Shells = _services.Require<XdgShell>();
        _capture.Attach(_services.Require<IToplevelModel>(), surface =>
            OwnerOf(surface) is { } card ? new ToplevelCaptureTrees(card.Slot, null) : null);
        var decorations = _services.Require<XdgDecorationManager>();
        decorations.DefaultMode = DecorationMode.ServerSide;
        decorations.ChooseMode = (_, _) => DecorationMode.ServerSide;

        _shadowTile = CardShadow.CreateTile();
        _black = CardShadow.CreateBlack();

        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = _capture,
            Frames = _services.Require<IFrameClock>(),
            Requested = options.Outputs,
            Scales = options.Scales,
            ContinuousRepaint = options.Frames > 0,
            NestedName = index => $"luna-wm-{index + 1}",
            HeadlessMode = new OutputMode(1024, 768, 60_000),
        };
        _outputs.ConfigureHandoff(config.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, config.Handoff.DurationMs, config.Handoff.HoldLimitMs);
        _loop = new CompositorRunLoop(_host, _outputs) { Outro = new LunaLogoutOutro(line => _report.Line(line)) };
        _outputs.Emptied += Stop;
        _outputs.HandoffInherited += inheritance => BasinReport.Line(HandoffLines.Inherit(inheritance));
        _host.HandoffRetained += retention => BasinReport.Line(HandoffLines.Retain(retention));
        _outputs.ModesetRefused += card => log.Error($"modeset refused by {card.Name} in every mode");
        _outputs.Added += OnOutputAdded;
        _outputs.Removed += OnOutputRemoved;
        _outputs.ModeChanged += driver =>
        {
            _report.Line($"MODE {driver.Output.Name} {driver.Width}x{driver.Height}");
            Relayout();
        };
        _outputs.LayoutChanged += Relayout;
        _outputs.Painted += TakeShot;
        _outputs.BeforeRepaint += driver => Advance(ViewOf(driver));

        _screens = new OutputScreens(_outputs, _layout);
        _gpu = _renderer.Device is { } uiDevice
            ? BasinGlGpu.TryCreate(uiDevice.DevicePath, uiDevice as Basin.Render.Gl.GlDevice, _renderer.DmabufTextureFormats)
            : null;
        _ui = BasinPlatform.Start<LunaApp>(new BasinPlatformOptions
        {
            EventLoop = _host.Loop,
            Screens = _screens,
            Selection = _services.Find<ISelectionStore>(),
            Theme = UIThemeVariant.Light,
            Gpu = _gpu,
            Configure = builder => AvaEnyo.AppBuilderExtensions.WithAvaEnyoFonts(builder),
        });
        _popupLayer = new SceneTree(_scene.Root);
        _uiDriver = new UIDriver(_ui, _host.Loop) { Index = _chromeIndex, PopupLayer = _popupLayer };

        _seat = new LunaSeat(_host, _services, this, _scene, _layout, servicePack.CursorTheme, inputSink, _chromeIndex, log);
        _inputConfigurator.Rejected += outcome =>
            log.Debug($"input: {InputSettingNames.Key(outcome.Setting)} on {outcome.DeviceName ?? "every device"}: {outcome.Result}");
        _seat.AttachConfigurator(_inputConfigurator);
        ApplyInput();

        AttachWindows();
        AttachLayers();
        AttachAppMenu();
        AttachKeyboard();
        if (xwayland is not null)
        {
            _xServer = xwayland.Native!.Server;
            xwayland.Configure(_config.XWaylandScale, _config.XWaylandDpiBuckets);
            foreach (var display in xwayland.Displays)
            {
                display.WindowManagerReady += AttachXWayland;
            }

            BasinReport.Line($"XWAYLAND {_xServer.DisplayName}");
            if (xwayland.LowDensity is { } lowDensity)
            {
                BasinReport.Line($"XWAYLAND LOW {lowDensity.DisplayName}");
            }
        }

        _outputs.CreateInitialOutputs();
        if (options.Backend == BackendKind.Drm && _views.Count == 0)
        {
            throw new InvalidOperationException("no connected output");
        }

        Relayout();
        if (_host.Parent is { } parent)
        {
            parent.ParentGone += Stop;
        }
    }

    internal Basin.Seat.Seat Seat { get; }

    internal XdgShell Shells { get; }

    internal LunaConfig Config => _config;

    internal OutputLayout Layout => _layout;

    internal OutputDriver Outputs => _outputs;

    internal IReadOnlyList<LunaView> Views => _views;

    internal AvaloniaUIHost Ui => _ui;

    internal UISurfaceIndex ChromeIndex => _chromeIndex;

    internal LunaSeat SeatInput => _seat;

    internal static LunaView ViewOf(OutputView driver) => (LunaView)driver.Tag!;

    internal LunaView PrimaryView => _views.Count > 0 ? _views[0] : throw new InvalidOperationException("no output");

    internal void Stop() => _loop.Stop();

    private void OnOutputAdded(OutputView driver)
    {
        var view = new LunaView(driver, _scene);
        driver.Tag = view;
        _views.Add(view);
        if (driver.Output is Basin.Backend.Wayland.WaylandOutput nested)
        {
            nested.SetTitle("luna-wm");
        }

        _report.Line($"OUTPUT {driver.Output.Name} {driver.Output.CurrentMode.Width}x{driver.Output.CurrentMode.Height}");
        AttachStatusBar(view);
        AttachChrome(view);
        view.Root.Enabled = true;
        _popupLayer.RaiseToTop();
        _sessionLockLayer.RaiseToTop();
        _seat.OutputAdded(driver);
    }

    private void OnOutputRemoved(OutputView driver)
    {
        if (driver.Output is Basin.Backend.Drm.DrmOutput card)
        {
            _report.Line($"OUTPUT - {card.Name}");
        }

        var view = ViewOf(driver);
        _views.Remove(view);
        _seat.OutputRemoved(driver);
        RehomeCards(view);
        view.Destroy();
    }

    private void Relayout()
    {
        foreach (var view in _views)
        {
            view.Reposition();
            PlaceStatusBar(view);
            ResizeCards(view);
            LayoutCards(view, animate: false);
            PlaceChrome(view);
        }

        RelayoutLayers();
        _outputs.ScheduleAll();
    }

    private int RunLoop()
    {
        BasinReport.Line(CompositorLines.Socket(_host.Socket));
        CurrentDesktop.Export("luna", _host.Drm is null ? null : _host.Socket);
        _seat.CenterPointer();
        _uiDriver.Woken += _outputs.ScheduleAll;
        _uiDriver.Start();
        var hangup = _host.Loop.AddSignal(Signal.Hangup, _ => Reload());
        WireIpc();
        StartClock();
        StartStatus();
        StartLauncher();
        StartNotifications();
        StartIdleLock();
        if (_options.Command is { Length: > 0 } command)
        {
            _initProcess = InitProcess.Start(
                command, _host.Socket, _xServer?.DisplayName, _log, _xwaylandModule?.ChildEnvironment(),
                _config.Launch.ForCommand(null, command) is { } density ? _xwaylandModule?.Route(density) : null);
        }

        _loop.Iterating += _uiDriver.Pump;
        _loop.Iterated += OnIterated;
        _loop.Frames = _options.Frames;
        _loop.Run();
        _loop.Iterated -= OnIterated;
        _loop.Iterating -= _uiDriver.Pump;
        _uiDriver.Woken -= _outputs.ScheduleAll;
        hangup.Remove();
        StopClock();
        StopStatus();
        StopNotifications();
        StopLock();
        UnwireIpc();
        return 0;
    }

    private void OnIterated()
    {
        ExpireCloseTimers();
        ApplyKeyboard();
    }

    private void Reload()
    {
        var next = LunaConfig.Load(_options.ConfigPath ?? LunaConfig.DefaultPath(), _options.NoConfig, _log, out var fatal);
        if (fatal is not null)
        {
            _log.Warn($"reload failed, keeping the running config: {fatal}");
            _report.Line("RELOAD failed");
            return;
        }

        var restart = string.Join(' ', _config.RestartKeysChanged(next));
        var previous = _config;
        _config = next;
        ApplyConfig();
        _outputs.ConfigureHandoff(next.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, next.Handoff.DurationMs, next.Handoff.HoldLimitMs);
        ReloadKeyboard(previous);
        _report.Line(restart.Length > 0 ? $"RELOAD restart-needed {restart}" : "RELOAD ok");
    }

    private Basin.XWayland.XWaylandModule? _xwaylandModule;

    private void ApplyConfig()
    {
        ApplyInput();
        _xwaylandModule?.Configure(_config.XWaylandScale, _config.XWaylandDpiBuckets);
        ApplyClockFormat();
        Relayout();
    }

    private void ApplyInput() => _inputConfigurator.Apply(_config.InputRules);

    internal void Spawn(string command)
    {
        if (_processes.SpawnShell(command, out var error) is null)
        {
            _log.Warn($"cannot start '{command}': {error}");
        }
    }

    internal void WriteScreenshot(string? path)
    {
        if (path is not { Length: > 0 } || _views.Count == 0)
        {
            return;
        }

        _uiDriver.Pump();
        if (SceneScreenshot.Write(_scene, _renderer, _views[0].Output, path))
        {
            _log.Info($"screenshot written to {path}");
        }
    }

    public void Dispose()
    {
        _xwayland.Dispose();
        _host.DisconnectClients();
        _ipc?.Dispose();
        _ipc = null;
        _colorPack.Luts.Dispose();
        _initProcess?.Stop(_log);
        _initProcess = null;
        _processes.TerminateAll(TimeSpan.FromMilliseconds(500));
        _processes.Dispose();
        StopAppMenu();
        StopKeyboard();
        StopLauncher();
        EndAllFlights();
        foreach (var card in _cards)
        {
            card.Dispose();
        }

        _cards.Clear();
        foreach (var view in _views)
        {
            ClearGhost(view);
            view.ReleaseChrome();
        }

        _inputConfigurator.Dispose();
        _seat.Dispose();
        _uiDriver.Dispose();
        _screens.Dispose();
        _ui.Dispose();
        _gpu?.Dispose();
        _outputs.Dispose();
        _scene.Root.Destroy();
        _shadowTile.Destroy();
        _black.Destroy();
        _services.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
