using Avalonia;
using AvaUnion;
using AvaUnion.BreezeIcons;
using AvaUnion.Engine;
using AvaUnion.Engine.Native;
using Basin;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.GLib;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;
using TabWm.Chrome;

namespace TabWm;

internal sealed partial class TabWm : IDisposable
{
    private readonly TabWmOptions _options;
    private readonly string _rendererName;
    private readonly TabWmConfig _config;
    private readonly BasinLogger _log;
    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private readonly SceneCurtain _curtain;
    private readonly BasinServices _services;
    private readonly TabLayers _layers;
    private readonly OutputDriver _outputs;
    private readonly CompositorRunLoop _loop;
    private readonly ColorCapabilityPack _colorPack;
    private readonly ToplevelGeometryReporter? _geometryReporter;
    private readonly BasinGlGpu? _gpu;
    private readonly AvaloniaUIHost _ui;
    private Basin.Accessibility.DesktopAccessibility? _access;
    private Basin.Portal.PortalSettingsService? _settingsPortal;
    private readonly Basin.Capabilities.Defaults.AppearanceSettingsStore _appearance = new();
    private readonly UIDriver _uiDriver;
    private readonly OutputScreens _screens;
    private readonly UISurfaceIndex _index = new();
    private readonly CursorController _cursor;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private readonly Basin.Seat.Backends.HookInputSink _inputSink = new();
    private readonly Basin.Seat.Backends.InputConfigurator _inputConfigurator = new();
    private readonly ProcessTracker _processes;
    private readonly DefaultActivationTokens _tokens = new();
    private readonly DecorationPreferences _decorationPreferences;
    private readonly TabWorkspaces _workspaces;
    private readonly GLibContextSource? _glib;
    private readonly SurfacePresenceTracker _presenceTracker;
    private readonly List<SurfaceBox> _presence = [];
    private Basin.XWayland.XWaylandServer? _xServer;
    private Basin.XWayland.XWaylandModule? _xwaylandModule;
    private FractionalScaleManager? _fractionalScale;
    private TabSeat? _seat;
    private ShellClock? _clock;
    private bool _stopping;

    public static int Run(TabWmOptions options, BasinLogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            var config = TabWmConfig.Load(options.ConfigPath, options.NoConfig, log, out var fatal);
            if (fatal is not null)
            {
                log.Error($"config: {fatal}");
                return 2;
            }

            using var tabWm = new TabWm(options, config, log);
            status = tabWm.RunLoop();
            rendered = tabWm._outputs.PrimaryRendered;
            tabWm.WriteScreenshot(options.Screenshot);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException)
        {
            log.Error(error, $"startup: the compositor did not start");
            return 1;
        }

        BasinReport.Line(CompositorLines.Frames(rendered));
        if (CompositorLines.Census() is { } census)
        {
            log.Error($"{census}");
        }

        return status;
    }

    internal TabWm(TabWmOptions options, TabWmConfig config, BasinLogger log)
    {
        _curtain = new SceneCurtain(_scene);
        _options = options;
        _config = config;
        _log = log;
        if (options.Engine is { } engine)
        {
            _config.Engine = engine switch
            {
                "native" => UnionEngineKind.Native,
                "managed" => UnionEngineKind.Managed,
                _ => UnionEngineKind.Auto,
            };
        }

        if (options.NoRestore)
        {
            _config.Restore = false;
        }

        var rendererName = options.Renderer;
        var stack = RendererCatalog.CreateWithFallback(ref rendererName, RendererCatalog.FindRenderNode(), fallback => fallback.Log(log));
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;
        _rendererName = rendererName;
        BasinReport.Line($"RENDERER {rendererName}");

        _host = BasinHost.Create(HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()) with
        {
            SocketFd = options.SocketFd,
            Handoff = options.Handoff.Resolve(HandoffMode.Auto),
        });
        _processes = new ProcessTracker(_host.Loop, () => ProcessTracker.SessionEnvironment(_host.Socket, _xServer?.DisplayName, _ipc?.Path, _xwaylandModule?.ChildEnvironment()))
        {
            X11Routes = density => _xwaylandModule?.Route(density),
        };

        _colorPack = new ColorCapabilityPack(_layout, _renderer);
        var servicePack = new DesktopServicePack(_scene, _layout, _renderer, _host.Drm) { ActivationTokens = _tokens };
        var capturePack = servicePack.Capture;
        _cursor = new CursorController(_layout) { Capture = capturePack.Capture, Curtain = _curtain };
        if (_host.Session is { } session && options.Backend == BackendKind.Drm)
        {
            _input = new Basin.Backend.Libinput.LibinputBackend(_host.Loop, session);
        }

        _services = _host.CreateServices()
            .Use(_layout)
            .With(servicePack)
            .With(_colorPack);
        _services.Install(DesktopPack.For("tab-wm"));
        if (_config.XWayland && !options.NoXWayland)
        {
            _xwaylandModule = new Basin.XWayland.XWaylandModule();
            _services.Install(_xwaylandModule);
        }

        if (_renderer.Device is { } renderDevice)
        {
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath, captureFormats: _renderer.DmabufRenderFormats));
        }

        _layers = new TabLayers(_scene.Root);
        _workspaces = new TabWorkspaces(this);
        _services.Use<IWorkspaceModel>(_workspaces);
        _services.Use<IToplevelPlacement>(new TabToplevelPlacement(this));
        _services.UseDefault<IInputDeviceConfiguration>(_inputConfigurator);
        CreateLayerDrivers();
        _services.Use<IInputSink>(_inputSink);
        _services.Use<IAppearanceSettings>(_appearance);
        ApplyAppearance();
        _services.Freeze();

        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = capturePack,
            Frames = _services.Require<IFrameClock>(),
            Requested = options.Outputs,
            Scales = options.Scales,
            ContinuousRepaint = options.Frames > 0,
            NestedName = index => $"tab-wm-{index + 1}",
            HeadlessMode = new OutputMode(1280, 720, 60_000),
        };
        _geometryReporter = _services.Find<IToplevelModel>() is { } toplevelModel
            ? new ToplevelGeometryReporter(_outputs, _scene, toplevelModel)
            : null;
        _loop = new CompositorRunLoop(_host, _outputs) { DispatchTimeoutMillis = -1 };
        _outputs.Emptied += Stop;
        _outputs.HandoffInherited += inheritance => BasinReport.Line(HandoffLines.Inherit(inheritance));
        _host.HandoffRetained += retention => BasinReport.Line(HandoffLines.Retain(retention));
        _outputs.ModesetRefused += card => log.Error($"output: {card.Name} refused a modeset in every mode");
        _outputs.Added += view => BasinReport.Line($"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        _presenceTracker = new SurfacePresenceTracker(_layout, (surface, scale) => _fractionalScale?.AnnounceScale(surface, scale));
        _outputs.Added += view => _presenceTracker.AddOutput(view.Output, view.Global);
        _outputs.Removed += view => _presenceTracker.RemoveOutput(view.Output);
        _outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene);
        _outputs.Added += view => _colorPack.AddOutput(view.Global, view.Output, view.Scene);
        _outputs.Removed += view => _colorPack.RemoveOutput(view.Global);
        _outputs.Removed += view => _cursor.RemoveOutput(view.Output);
        _outputs.Added += OnOutputAdded;
        _outputs.Removed += OnOutputRemoved;
        _outputs.LayoutChanged += Arrange;
        _outputs.BeforeRepaint += _ =>
        {
            UpdateSurfacePresence();
            _uiDriver?.SyncPopups();
        };

        _glib = GLibContextSource.TryAttach(_loop, _host.Loop, log);
        _screens = new OutputScreens(_outputs, _layout);
        _gpu = _renderer.Device is { } uiDevice
            ? BasinGlGpu.TryCreate(uiDevice.DevicePath, uiDevice as Basin.Render.Gl.GlDevice, _renderer.DmabufTextureFormats)
            : null;
        var unionOptions = new AvaUnionOptions { Engine = _config.Engine, HostRunsGLibMainLoop = _glib is not null };
        _ui = BasinPlatform.Start<TabWmApp>(new BasinPlatformOptions
        {
            EventLoop = _host.Loop,
            Screens = _screens,
            Selection = _services.Find<ISelectionStore>(),
            Theme = _config.Variant == ThemeVariantChoice.Dark ? UIThemeVariant.Dark : UIThemeVariant.Light,
            Gpu = _gpu,
            Configure = builder => builder.WithAvaUnion(unionOptions).WithBreezeIcons(),
        });
        ConfigureUnion();
        _services.Find<Basin.Capabilities.Defaults.UIElementDirectory>()?.Add(_ui);
        _uiDriver = new UIDriver(_ui, _host.Loop)
        {
            PopupLayer = _layers.Popups,
            Index = _index,
        };

        Seat = _services.Require<Basin.Seat.Seat>();
        Shell = _services.Require<XdgShell>();
        _fractionalScale = _services.Find<FractionalScaleManager>();
        WireColor();
        Decorations = _services.Require<XdgDecorationManager>();
        Decorations.DefaultMode = DecorationMode.ClientSide;
        Decorations.ChooseMode = (_, preference) => preference ?? DecorationMode.ServerSide;
        _decorationPreferences = new DecorationPreferences(
            Decorations, _services.Find<KdeServerDecorationManager>(), _services.Find<XdgToplevelSource>());

        _outputs.CreateInitialOutputs();
        _outputs.AdoptSecondaryCards();
        if (_host.Parent is not null)
        {
            _cursor.UseParentCursor();
        }

        _cursor.ColorProfiles = _services.Find<IColorProfileService>();
        if (_services.Find<CursorShapeManager>() is { } shapes)
        {
            _cursor.Shapes = shapes;
            shapes.CursorRequested += _cursor.ShowImage;
        }

        Seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;
        _cursor.Load(new ShmAllocator(), 64, 64, 24);
        servicePack.CursorTheme.Images = _cursor.Images;
        Seat.Keyboard.SetKeymap(Basin.Seat.SystemKeymap.Read());
        _seat = new TabSeat(_host, _services, _layout, _scene, _cursor, _index, _input, _inputSink)
        {
            KeyHook = OnKey,
            ButtonHook = OnButton,
            FrameButtonHook = OnFrameButton,
            FrameCursorAt = FrameCursorName,
            OwnsFrame = OwnsFrame,
            GrabActive = () => _grab.Active,
            PointerMoved = OnPointerMoved,
        };
        _inputConfigurator.Attach(Seat, _seat.Binder, _input?.Configuration);
        _seat.Binder.UseKeyboardAids(_host.Loop);
        BasinReport.Line($"KEYMAP system compiled={(Seat.Keyboard.Keymap is null ? "no" : "yes")}");

        AttachWindows();
        AttachXWayland();
        WireLayerShell();
        capturePack.Attach(_services.Require<IToplevelModel>(), surface =>
            WindowOwning(surface)?.Tree is { } tree ? new ToplevelCaptureTrees(tree, null) : null);
        _restacked = capturePack.Stack.RaiseChanged;

        if (options.Backend == BackendKind.Drm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("No output is connected. Connect a display, then start the compositor again.");
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += Stop;
        }

        ReportUnion();
        _access = new Basin.Accessibility.DesktopAccessibility(new Basin.Accessibility.DesktopAccessibilityOptions
        {
            Name = "tab-wm",
            Host = _host,
            Scene = _scene,
            Seat = Seat,
            Pointer = _seat.Binder.Pointer,
            Processes = _processes,
            TextInput = _services.Find<Basin.Desktop.TextInputManager>(),
            Toplevels = Toplevels,
            Aids = _seat.Binder.Aids,
            StartToolkit = _ui.StartAccessibilityAsync,
            Log = _log,
        });
        _access.Apply(_config.Accessibility);
        _ = StartAccessibilityAsync();
        if (_host.Drm is not null)
        {
            _settingsPortal = Basin.Portal.PortalSettingsService.TryStart(_host.Loop, _appearance, null, null, out _);
        }
    }

    internal Basin.Seat.Seat Seat { get; }

    internal XdgShell Shell { get; }

    internal XdgDecorationManager Decorations { get; }

    internal TabLayers Layers => _layers;

    internal IReadOnlyList<TabSet> Sets => _sets;

    internal IToplevelModel? Toplevels => _services.Find<IToplevelModel>();

    internal void Stop()
    {
        _stopping = true;
        _loop.Stop();
    }

    internal void WriteScreenshot(string? screenshotPath)
    {
        if (screenshotPath is not { Length: > 0 } path || _outputs.Views.FirstOrDefault() is not { } view)
        {
            return;
        }

        if (SceneScreenshot.Write(_scene, _renderer, view.Output, path))
        {
            _log.Info($"screenshot: written to {path}");
        }
    }

    private void ConfigureUnion() => ApplyTheme();

    private void ReportUnion()
    {
        var engine = EngineSelector.Selected ?? AvaUnion.Elements.UnionEnvironment.Engine;
        var pump = engine is NativeEngine native
            ? native.PumpMode == 1 ? "shared" : "timer"
            : "managed";
        var name = engine.Kind == UnionEngineKind.Native ? "native" : "managed";
        BasinReport.Line($"UNION engine={name} pump={pump} glib={(_glib is null ? "no" : "yes")}");
        if (engine.Kind != UnionEngineKind.Native && EngineSelector.NativeUnavailableReason is { } reason)
        {
            _log.Info($"union: the native engine is not in use: {reason}");
        }
        else if (pump == "timer")
        {
            _log.Info($"union: Qt runs on AvaUnion's timer pump, so an idle compositor wakes several times a second");
        }
    }

    private int RunLoop()
    {
        BasinReport.Line(CompositorLines.Socket(_host.Socket));
        CurrentDesktop.Export("basin", _host.Drm is null ? null : _host.Socket);
        _seat?.CenterPointer();
        _uiDriver.Woken += _outputs.ScheduleAll;
        _uiDriver.Start();
        _clock = new ShellClock(_host.Loop, now => now.ToString("HH:mm"), text =>
        {
            foreach (var set in _sets)
            {
                set.Strip.Clock = text ?? string.Empty;
            }
        });
        _clock.Refresh();
        WireIpc();
        StartLauncher();
        StartAudio();
        StartSession();

        _loop.Iterating += _uiDriver.Pump;
        _loop.Frames = _options.Frames;
        _loop.Run();
        _loop.Iterating -= _uiDriver.Pump;

        SaveSession("quit");
        _uiDriver.Woken -= _outputs.ScheduleAll;
        _ipc?.Dispose();
        _ipc = null;
        _access?.Dispose();
        _access = null;
        _settingsPortal?.Dispose();
        _settingsPortal = null;
        _settle?.Dispose();
        _clock?.Dispose();
        return 0;
    }

    private async Task StartAccessibilityAsync()
    {
        if (_access is { } access && await access.StartAsync(_config.Accessibility.Enabled) && _config.Accessibility.ScreenReader == "on")
        {
            _host.Loop.AddIdle(PlayReadyChime);
        }
    }

    private void WireColor()
    {
        if (!_colorPack.Attach(_services, _scene))
        {
            _log.Info($"color: no color manager, color management is off");
            return;
        }

        _colorPack.Surfaces.CountChanged += attached => _report.Line(CompositorLines.Luts(attached));
        _colorPack.Manager.OutputDescriptionChanged += (global, description) => _cursor.Describe(global.Output, description);
    }

    private void RefreshSurfaceLuts() => _colorPack.RefreshSurfaces();

    private void UpdateSurfacePresence()
    {
        if (_fractionalScale is null)
        {
            return;
        }

        _scene.CollectSurfaces(_presence);
        _presenceTracker.Update(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_presence));
        _presence.Clear();
    }

    public void Dispose()
    {
        _host.DisconnectClients();
        _ipc?.Dispose();
        _ipc = null;
        _settle?.Dispose();
        StopAudio();
        StopLauncher();
        _sessionTimer?.Remove();
        _closeTimer?.Remove();
        _colorPack.Luts.Dispose();
        _processes.TerminateAll(TimeSpan.FromMilliseconds(500));
        _processes.Dispose();
        _xdriver?.Dispose();
        _inputConfigurator.Dispose();
        _seat?.Dispose();
        DisposeChrome();
        _uiDriver.Dispose();
        _screens.Dispose();
        _cursor.Dispose();
        _input?.Dispose();
        _glib?.Dispose();
        _ui.Dispose();
        _gpu?.Dispose();
        _geometryReporter?.Dispose();
        _outputs.Dispose();
        _scene.Root.Destroy();
        _services.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
