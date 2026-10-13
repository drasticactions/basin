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
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed partial class System7Shell : IDisposable
{
    private readonly System7Options _options;
    private readonly BasinLogger _log;
    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private readonly BasinServices _services;
    private readonly System7Layers _layers;
    private readonly OutputDriver _outputs;
    private readonly CompositorRunLoop _loop;
    private readonly BasinGlGpu? _gpu;
    private readonly AvaloniaUIHost _ui;
    private readonly List<HostWindow> _hostWindows = [];
    private readonly OutputScreens _screens;
    private readonly UISurfaceIndex _chrome = new();
    private readonly UIDriver _uiDriver;
    private readonly CursorController _cursor;
    private readonly System7Cursors _cursors;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private readonly Basin.Seat.Backends.InputConfigurator _inputConfigurator = new();
    private readonly System7Seat _seat;
    private readonly DesktopPattern _pattern;
    private readonly ProcessTracker _processes;
    private InitProcess? _initProcess;
    private readonly Basin.Desktop.ColorCapabilityPack _colorPack;
    private System7Config _config;
    private Basin.XWayland.XWaylandServer? _xServer;
    private IEventSource? _watchTimer;

    public static int Run(System7Options options, BasinLogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            var config = System7Config.Load(options.ConfigPath, options.NoConfig, log, out var fatal);
            if (fatal is not null)
            {
                log.Error($"{fatal}");
                return 1;
            }

            using var shell = new System7Shell(options, log, config);
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

    internal System7Shell(System7Options options, BasinLogger log, System7Config config)
    {
        _options = options;
        _log = log;
        _config = config;

        var name = options.Renderer;
        var stack = RendererCatalog.CreateWithFallback(ref name, RendererCatalog.FindRenderNode(), fallback => log.Warn($"{fallback.Describe()}"));
        _renderer = stack.Renderer;
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
        var servicePack = new DesktopServicePack(_scene, _layout, _renderer, _host.Drm) { Bell = new AlertBell(Beep) };
        var capturePack = servicePack.Capture;
        _cursor = new CursorController(_layout) { Capture = capturePack.Capture };
        if (_host.Session is { } session && options.Backend == BackendKind.Drm)
        {
            _input = new Basin.Backend.Libinput.LibinputBackend(_host.Loop, session);
        }

        _services = _host.CreateServices()
            .Use(_layout)
            .With(servicePack)
            .With(_colorPack);
        _services.Install(DesktopPack.For("system7-wm"));
        Basin.XWayland.XWaylandModule? xwayland = null;
        if (options.XWayland ?? _config.XWayland)
        {
            xwayland = new Basin.XWayland.XWaylandModule { IncludeLowDensity = _config.XWaylandLowDensity };
            _xwaylandModule = xwayland;
            _services.Install(xwayland);
        }
        else if (Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 } inherited)
        {
            Environment.SetEnvironmentVariable("DISPLAY", null);
            _log.Info($"no XWayland: DISPLAY={inherited} is not passed on, so an X11 client cannot open on the parent's X server");
        }

        if (_renderer.Device is { } renderDevice)
        {
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath, captureFormats: _renderer.DmabufRenderFormats));
        }

        InstallShellModules();
        _layers = new System7Layers(_scene.Root);
        CreateLayerDrivers();
        _services.UseDefault<IInputDeviceConfiguration>(_inputConfigurator);
        _densityControl = new Basin.Config.LaunchRuleDensityControl(
            () => _services.Find<IToplevelModel>(), _processes, _host.Loop,
            () => _options.NoConfig ? null : _options.ConfigPath ?? System7Config.DefaultPath(), _log)
        {
            DesktopIdForAppId = appId => _desktopEntries.FindForAppId(appId)?.Id,
        };
        _densityControl.RulesWritten += rules => _config.Launch = rules;
        _services.Use<IX11DensityControl>(_densityControl);
        _services.Freeze();

        _pattern = new DesktopPattern(_layers.Pattern)
        {
            Depth = _config.Depth,
            Pattern = System7DesktopPattern.Find(_config.Pattern) ?? System7DesktopPattern.Default,
            Wallpaper = _config.Wallpaper,
        };

        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = capturePack,
            Frames = _services.Require<IFrameClock>(),
            Requested = options.Outputs,
            Scales = options.Scales,
            ContinuousRepaint = options.Frames > 0,
            NestedName = index => $"system7-wm-{index + 1}",
            DecorateNested = false,
            HeadlessMode = new OutputMode(1024, 768, 60_000),
        };
        _outputs.ConfigureHandoff(config.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, config.Handoff.DurationMs, config.Handoff.HoldLimitMs);
        _loop = new CompositorRunLoop(_host, _outputs);
        _outputs.Emptied += Stop;
        _outputs.HandoffInherited += inheritance => BasinReport.Line(HandoffLines.Inherit(inheritance));
        _host.HandoffRetained += retention => BasinReport.Line(HandoffLines.Retain(retention));
        _outputs.ModesetRefused += card => log.Error($"modeset refused by {card.Name} in every mode");
        _outputs.Added += view =>
        {
            if (view.Output is Basin.Backend.Wayland.WaylandOutput nested)
            {
                nested.SetTitle("system7-wm");
                nested.HostFrameAvailable += frame =>
                {
                    var window = new HostWindow(frame, _ui!, "system7-wm", _config.Depth, Stop);
                    _hostWindows.Add(window);
                    nested.Committed += window.OnOutputChanged;
                };
            }

            BasinReport.Line($"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
            _cursor.AddOutput(view.Output, view.Scene);
            if (_config.DepthApplications)
            {
                ApplyApplicationDepth();
            }
        };
        _outputs.Removed += view =>
        {
            _usable.Remove(view.Output);
            _cursor.RemoveOutput(view.Output);
            _pattern.Remove(view.Output);
        };
        _outputs.LayoutChanged += Relayout;
        _outputs.Painted += TakeShot;

        _screens = new OutputScreens(_outputs, _layout);
        _gpu = _renderer.Device is { } uiDevice
            ? BasinGlGpu.TryCreate(uiDevice.DevicePath, uiDevice as Basin.Render.Gl.GlDevice, _renderer.DmabufTextureFormats)
            : null;
        _ui = BasinPlatform.Start<ShellApp>(new BasinPlatformOptions
        {
            EventLoop = _host.Loop,
            Screens = _screens,
            Selection = _services.Find<ISelectionStore>(),
            Theme = UIThemeVariant.Light,
            Gpu = _gpu,
        });
        _uiDriver = new UIDriver(_ui, _host.Loop) { Index = _chrome, PopupLayer = _layers.Popups };

        _cursors = new System7Cursors(_cursor);
        if (_host.Parent is not null)
        {
            _cursor.UseParentCursor();
        }

        if (_services.Find<CursorShapeManager>() is { } shapes)
        {
            _cursor.Shapes = shapes;
            shapes.CursorRequested += _cursor.ShowImage;
        }

        Seat = _services.Require<Basin.Seat.Seat>();
        Seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;
        _cursor.Load(new ShmAllocator(), 64, 64, 16);
        servicePack.CursorTheme.Images = _cursor.Images;
        _cursor.ShowNamed("default");

        _seat = new System7Seat(_host, _services, _layout, _scene, _cursor, _chrome, _input)
        {
            KeyHook = OnKey,
            PressHook = OnPress,
            GrabMotion = OnGrabMotion,
            GrabRelease = OnGrabRelease,
            CursorHook = CursorAt,
        };
        _inputConfigurator.Rejected += outcome =>
            log.Debug($"input: {Basin.Capabilities.InputSettingNames.Key(outcome.Setting)} on {outcome.DeviceName ?? "every device"}: {outcome.Result}");
        _inputConfigurator.Attach(Seat, _seat.Binder, _input?.Configuration);
        ApplyInput();

        Shells = _services.Require<XdgShell>();
        AttachWindows(capturePack);
        AttachLayers();
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

        WireColor();
        _outputs.CreateInitialOutputs();
        if (Environment.GetEnvironmentVariable("BASIN_DRM_DEVICE") is null)
        {
            _outputs.AdoptSecondaryCards();
        }
        if (options.Backend == BackendKind.Drm && _outputs.Views.Count == 0)
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

    internal System7Config Config => _config;

    internal OutputDriver Outputs => _outputs;

    internal OutputLayout Layout => _layout;

    internal System7Seat SeatInput => _seat;

    internal void Stop() => _loop.Stop();

    internal OutputView? MainView
    {
        get
        {
            foreach (var view in _outputs.Views)
            {
                if (_config.MainOutput.Length > 0 && view.Output.Name == _config.MainOutput)
                {
                    return view;
                }
            }

            return _outputs.Views.Count > 0 ? _outputs.Views[0] : null;
        }
    }

    internal Box MainBox => MainView is { } view ? _layout.BoxOf(view.Output) : new Box(0, 0, 1024, 768);

    internal System7ColorDepth Depth => _config.Depth;

    private void Relayout()
    {
        foreach (var view in _outputs.Views)
        {
            _pattern.Place(view.Output, _layout.BoxOf(view.Output));
        }

        RelayoutLayers();
        RelayoutChrome();
        _outputs.ScheduleAll();
    }

    private int RunLoop()
    {
        BasinReport.Line(CompositorLines.Socket(_host.Socket));
        CurrentDesktop.Export("basin", _host.Drm is null ? null : _host.Socket);
        _seat.CenterPointer();
        _uiDriver.Woken += _outputs.ScheduleAll;
        _uiDriver.Start();
        var hangup = _host.Loop.AddSignal(Signal.Hangup, _ => Reload());
        _watchTimer = _host.Loop.AddTimer(OnWatchTick);
        WireIpc();
        StartClock();
        if (_options.Command is { Length: > 0 } command)
        {
            _initProcess = InitProcess.Start(
                command, _host.Socket, _xServer?.DisplayName, _log, _xwaylandModule?.ChildEnvironment(),
                _config.Launch.ForCommand(null, command) is { } density ? _xwaylandModule?.Route(density) : null);
        }

        _loop.Iterating += _uiDriver.Pump;
        _loop.Frames = _options.Frames;
        _loop.Run();
        _loop.Iterating -= _uiDriver.Pump;
        _uiDriver.Woken -= _outputs.ScheduleAll;
        hangup.Remove();
        _watchTimer?.Remove();
        StopClock();
        UnwireIpc();
        return 0;
    }

    private void OnWatchTick()
    {
        if (_cursor.Showing is "wait" or "watch" or "progress" or "left_ptr_watch")
        {
            _cursors.AdvanceWatch();
        }

        _watchTimer?.UpdateTimer((int)_cursors.WatchInterval.TotalMilliseconds);
    }

    private void Reload()
    {
        var next = System7Config.Load(_options.ConfigPath ?? System7Config.DefaultPath(), _options.NoConfig, _log, out var fatal);
        if (fatal is not null)
        {
            _log.Warn($"reload failed, keeping the running config: {fatal}");
            BasinReport.Line("RELOAD failed");
            return;
        }

        var restart = string.Join(' ', _config.RestartKeysChanged(next));
        _config = next;
        ApplyConfig();
        _outputs.ConfigureHandoff(next.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, next.Handoff.DurationMs, next.Handoff.HoldLimitMs);
        BasinReport.Line(restart.Length > 0 ? $"RELOAD restart-needed {restart}" : "RELOAD ok");
    }

    private Basin.XWayland.XWaylandModule? _xwaylandModule;
    private Basin.Config.LaunchRuleDensityControl? _densityControl;

    internal void ApplyConfig()
    {
        ApplyInput();
        _xwaylandModule?.Configure(_config.XWaylandScale, _config.XWaylandDpiBuckets);
        _pattern.Depth = _config.Depth;
        _pattern.Pattern = System7DesktopPattern.Find(_config.Pattern) ?? System7DesktopPattern.Default;
        _pattern.Wallpaper = _config.Wallpaper;
        ApplyDepth();
        foreach (var window in _hostWindows)
        {
            window.SetDepth(_config.Depth);
        }

        Relayout();
    }

    internal void Spawn(string command, string? desktopId = null)
    {
        if (_processes.Spawn(new ProcessLaunch(["/bin/sh", "-c", command]) { DesktopId = desktopId }, out var error) is null)
        {
            _log.Error($"cannot start {command}: {error}");
        }
    }

    internal void WriteScreenshot(string? path)
    {
        if (path is not { Length: > 0 } || MainView is not { } view)
        {
            return;
        }

        _uiDriver.Pump();
        if (SceneScreenshot.Write(_scene, _renderer, view.Output, path))
        {
            _log.Info($"screenshot written to {path}");
        }
    }

    public void Dispose()
    {
        _host.DisconnectClients();
        _ipc?.Dispose();
        _ipc = null;
        _colorPack.Luts.Dispose();
        _initProcess?.Stop(_log);
        _initProcess = null;
        _processes.TerminateAll(TimeSpan.FromMilliseconds(500));
        _processes.Dispose();
        DisposeShell();
        _inputConfigurator.Dispose();
        _seat.Dispose();
        _uiDriver.Dispose();
        _screens.Dispose();
        _cursor.Dispose();
        _input?.Dispose();
        _pattern.Dispose();
        _registrar?.Dispose();
        _menuBus?.Dispose();
        DisposeVolume();
        foreach (var window in _hostWindows)
        {
            window.Dispose();
        }

        _hostWindows.Clear();
        DisposeApplicationDepth();
        _ui.Dispose();
        _gpu?.Dispose();
        _outputs.Dispose();
        _scene.Root.Destroy();
        _services.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
