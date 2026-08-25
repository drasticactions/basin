using System.Diagnostics;
using Basin;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Effects;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Plasma;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;
using Wayland.Server;

namespace PlasmaHost;

internal sealed partial class PlasmaHost : IDisposable, IBell
{
    private readonly PlasmaHostOptions _options;
    private readonly BasinLogger _log;
    private readonly IRenderer _renderer;
    private readonly PlasmaHostBackdrops _backdrops;
    private readonly KwinEffectsConfig _effectsConfig = KwinEffectsConfig.Load();
    private readonly PlasmaHostAnimations _animations;
    private readonly PlasmaHostStages _stages;
    private PlasmaHostFeedback? _feedback;
    private DimInactiveEffect? _dim;
    private IPixelShader? _dimShader;
    private readonly Dictionary<Surface, SceneSurface> _sceneSurfaces = [];
    private readonly IAllocator? _deviceAllocator;
    private readonly Basin.Host.BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private PointerRefresh? _pointerRefresh;
    private readonly BasinServices _services;
    private readonly OutputDriver _outputs;
    private Basin.Color.ColorCapabilityPack _colorPack = null!;
    private readonly CompositorRunLoop _loop;
    private readonly PlasmaShellPlacement _placement;
    private readonly PlasmaShellManager _manager;
    private readonly Basin.Desktop.FractionalScaleManager _fractionalScale;
    private readonly PlasmaHostWindows _windows;
    private readonly PlasmaHostSeat _seat;
    private readonly Basin.Seat.Seat _coreSeat;
    private readonly Basin.Desktop.SessionLockManager _sessionLock;
    private readonly ILockOverlaySurfaces _lockOverlays;
    private Basin.Desktop.SessionLockSceneDriver _lockDriver = null!;
    private readonly List<SceneSurface> _overlayScenes = [];
    private readonly List<SlideEffect> _slides = [];
    private readonly PlasmaHostScreencast? _screencast;
    private readonly OutputScreens _screens;
    private readonly BasinGlGpu? _gpu;
    private readonly AvaloniaUIHost _ui;
    private readonly UIDriver _uiDriver;
    private readonly UISurfaceIndex _uiSurfaces = new();
    private readonly BreezeTheme _theme;
    private readonly KdeConfigNotify _notify;
    private readonly PlasmaHostFrames _frames;
    private readonly PlasmaHostDesktops _desktops;
    private readonly PlasmaDesktopOsd _osd;
    private readonly PlasmaWindowsShell _overview;
    private readonly PlasmaTilesEditor _tiles;
    private readonly Basin.Backend.Headless.HeadlessBackend _virtualBackend;
    private StdinCommands? _stdin;
    private LinuxDmabufGlobal? _dmabufGlobal;
    private readonly HashSet<Surface> _offloadFeedback = [];
    private readonly List<Surface> _offloadFeedbackGone = [];
    private Process? _shell;
    private KwinOutputSettings? _kdeOutputs;
    private string? _kdeOutputsPath;
    private readonly HashSet<IOutput> _placedOutputs = [];
    private bool _kdeOutputsApplying;
    private bool _commandLocked;

    public static int Run(PlasmaHostOptions options, BasinLogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var host = new PlasmaHost(options, log);
            status = host.RunLoop();
            rendered = host._outputs.PrimaryRendered;
            host.WriteScreenshot(options.Screenshot);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException)
        {
            log.Error($"{error.Message}");
            return 1;
        }

        BasinReport.Line(CompositorLines.Frames(rendered));
        if (BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0))
        {
            log.Error($"{BasinCounters.CensusReport()}");
        }

        return status;
    }

    internal PlasmaHost(PlasmaHostOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;

        var rendererName = options.Renderer;
        var stack = RendererCatalog.CreateWithFallback(
            ref rendererName,
            RendererCatalog.FindRenderNode(),
            fallback => log.Warn($"{(fallback.Describe())}"));
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;

        _host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()));

        _colorPack = new Basin.Color.ColorCapabilityPack(_layout);
        var servicePack = new DesktopServicePack(_scene, _layout, _renderer, _host.Drm) { Bell = this };
        var capturePack = servicePack.Capture;
        var cursorTheme = servicePack.CursorTheme;
        var inputSink = new Basin.Seat.Backends.HookInputSink();
        _desktops = new PlasmaHostDesktops(_layout, DesktopCount);
        _backdrops = new PlasmaHostBackdrops(_renderer);
        _animations = new PlasmaHostAnimations(_effectsConfig);
        _stages = new PlasmaHostStages(_effectsConfig, _renderer);
        _services = _host.CreateServices()
            .Use<IWorkspaceModel>(_desktops)
            .Use(_layout)
            .Use(_scene)
            .With(servicePack)
            .With(_colorPack)
            .Use<IInputSink>(inputSink)
            .Use<Basin.Capabilities.IFakeInputAuthority>(new PlasmaHostFakeInputAuthority());

        if (_backdrops.Capability is { } backgroundEffects)
        {
            _services.Use(backgroundEffects);
        }

        if (OperatingSystem.IsLinux() &&
            PlasmaHostScreencast.TryCreate(_host.Loop, capturePack.Capture, _layout, log) is { } screencast)
        {
            _screencast = screencast;
            _services.Use<IScreencastPublisher>(screencast);
            capturePack.Capture.AddDamageObserver(screencast);
        }

        _virtualBackend = new Basin.Backend.Headless.HeadlessBackend(_host.Loop);
        var virtualOutputs = new PlasmaHostVirtualOutputs(_virtualBackend);
        _services.Use<IVirtualOutputFactory>(virtualOutputs);

        var pack = (DesktopPack.For("plasma-host") + PlasmaPack.Default)
            .Without("org_kde_kwin_server_decoration_manager");
        _services.Install(pack);
        _services.Install(
            new Basin.Desktop.KdeServerDecorationModule(
                Basin.Desktop.KdeServerDecorationManager.DecorationMode.Server));
        LinuxDmabufModule? dmabufModule = null;
        if (_renderer.Device is { } renderDevice)
        {
            dmabufModule = new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath);
            _services.Install(dmabufModule);
        }

        _services.Freeze();
        _dmabufGlobal = dmabufModule?.Global;

        _placement = _services.Require<PlasmaShellPlacement>();
        _manager = _services.Require<PlasmaShellManager>();
        _fractionalScale = _services.Require<Basin.Desktop.FractionalScaleManager>();

        var seat = _services.Require<Basin.Seat.Seat>();
        var decorations = _services.Require<XdgDecorationManager>();
        decorations.DefaultMode = DecorationMode.ServerSide;
        decorations.ChooseMode = (_, preference) => preference ?? DecorationMode.ServerSide;

        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = capturePack,
            Frames = _services.Require<IFrameClock>(),
            Requested = options.Outputs,
            Scales = options.Scales,
            ContinuousRepaint = options.Frames > 0,
            NestedName = index => $"plasma-host-{index + 1}",
            HeadlessMode = new OutputMode(1920, 1080, 60_000),
        };
        virtualOutputs.Outputs = _outputs;
        _presenceTracker = new SurfacePresenceTracker(_layout, _fractionalScale.AnnounceScale);
        _outputs.Added += view => _presenceTracker.AddOutput(view.Output, view.Global);
        _outputs.Removed += view => _presenceTracker.RemoveOutput(view.Output);
        _loop = new CompositorRunLoop(_host, _outputs);
        _outputs.Emptied += Stop;
        _outputs.ModesetRefused += card =>
            log.Error($"modeset refused by {card.Name} in every mode");
        _outputs.Added += view => BasinReport.Line($"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        _outputs.Painted += _ => UpdateSurfacePresence();
        _outputs.LayoutChanged += UpdateSurfacePresence;
        _outputs.Added += view => _stages.Attach(view.Scene!);
        _outputs.Added += view => view.Scene!.OffloadCandidatesChanged += candidates => OnOffloadCandidates(view, candidates);
        _outputs.Removed += view =>
        {
            if (view.Scene is { } scene)
            {
                _stages.Detach(scene);
            }
        };
        _outputs.Added += view => view.Scene!.BeforeRepaint += tick =>
        {
            var animating = false;
            for (var i = 0; i < _slides.Count; i++)
            {
                animating |= _slides[i].Step(tick);
            }

            animating |= StepShadows(tick);
            animating |= _animations.Step(tick);
            animating |= _feedback?.Step(tick) ?? false;
            animating |= _stages.Step(
                tick,
                _seat?.PointerX ?? 0,
                _seat?.PointerY ?? 0,
                view.Output.CurrentMode.Width,
                view.Output.CurrentMode.Height);

            if (animating)
            {
                if (_stages.NeedsFullRepaint(view.Scene!) || _animations.IsRunning)
                {
                    view.Scene!.Ring.AddWhole();
                }

                view.Scheduler?.ScheduleRepaint();
            }
        };

        _theme = new BreezeTheme();
        _screens = new OutputScreens(_outputs, _layout);
        _gpu = _renderer.Device is { } uiDevice
            ? BasinGlGpu.TryCreate(
                uiDevice.DevicePath, uiDevice as Basin.Render.Gl.GlDevice, _renderer.DmabufTextureFormats)
            : null;
        _ui = BasinPlatform.Start<Shell.PlasmaApp>(new BasinPlatformOptions
        {
            EventLoop = _host.Loop,
            Screens = _screens,
            Selection = _services.Find<ISelectionStore>(),
            Gpu = _gpu,
            Theme = _theme.Variant,
        });
        log.Info($"chrome renders into {_ui.Produces} on {(_gpu is null ? "the CPU" : _renderer.Device?.DevicePath ?? "a render node")}");
        _uiDriver = new UIDriver(_ui, _host.Loop)
        {
            PopupLayer = _placement.Overlay,
            Index = _uiSurfaces,
        };
        _frames = new PlasmaHostFrames(_ui, _uiSurfaces, _theme);
        _osd = new PlasmaDesktopOsd(
            _ui, _uiSurfaces, _placement.Overlay, _desktops, _layout, _host.Loop, _theme);
        _osd.Repaint += _outputs.ScheduleAll;
        _windows = new PlasmaHostWindows(_layout, seat, _placement, _manager);
        _overview = new PlasmaWindowsShell(
            _ui, _uiSurfaces, _placement.Overlay, _windows, _desktops, _layout, _theme);
        _overview.Repaint += _outputs.ScheduleAll;
        _tiles = new PlasmaTilesEditor(_ui, _uiSurfaces, _placement.Overlay, _layout, _theme);
        _tiles.Repaint += _outputs.ScheduleAll;
        _desktops.Windows = _windows;
        _windows.OnCurrentDesktop = view => _desktops.IndexOf(view) == _desktops.Current;
        _windows.ViewMapped += _desktops.Adopt;
        _windows.ViewRemoved += _desktops.Forget;
        _windows.WireDecorations(
            decorations,
            _services.Require<Basin.Desktop.KdeServerDecorationManager>(),
            _services.Require<ServerDecorationPaletteManager>(),
            _frames);
        _services.Require<XdgToplevelIconManager>().IconChanged += _windows.SetIconName;
        var shadows = _services.Require<ShadowManager>();
        var slides = _services.Require<SlideManager>();
        _feedback = new PlasmaHostFeedback(_effectsConfig, _placement.Layers.Feedback)
        {
            Now = () => new FrameTick(
                _outputs.Views.Count > 0 ? _outputs.Views[0].Scheduler?.PredictedVblankNanos ?? 0 : 0, 0),
            BellArea = ringing =>
            {
                var view = (ringing is null ? null : _windows.ViewFor(ringing)) ?? _windows.FocusedView;
                return view is { } window
                    ? new Box(
                        window.Tree.ScenePosition.X,
                        window.Tree.ScenePosition.Y,
                        window.GeometrySize().Width,
                        window.GeometrySize().Height)
                    : _outputs.Views.Count > 0 ? _layout.BoxOf(_outputs.Views[0].Output) : default;
            },
        };
        _backdrops.Bind(
            _services.Require<BlurManager>(),
            _services.Require<ContrastManager>(),
            _services.Require<BackgroundEffectManager>());
        _backdrops.Corners = new BlurCorners(BreezeMetrics.CornerRadius);
        _backdrops.Options = ReadBlurOptions();
        void AttachEffects(SceneSurface scene)
        {
            _sceneSurfaces[scene.Surface] = scene;
            scene.Destroyed += () => _sceneSurfaces.Remove(scene.Surface);
            _backdrops.Attach(scene);
            _ = new ShadowEffect(scene, shadows);
            var slide = new SlideEffect(scene, slides, _ => ScreenAreaOf(scene));
            slide.Started += _outputs.ScheduleAll;
            _slides.Add(slide);
            scene.Destroyed += () => _slides.Remove(slide);
        }

        _windows.SceneCreated += AttachEffects;
        _placement.SceneCreated += (shell, scene) => AttachEffects(scene);
        _windows.Attach(
            _services.Require<XdgShell>(),
            _services.Require<XdgToplevelSource>(),
            _services.Require<LayerShell>());
        var toplevelSource = _services.Require<XdgToplevelSource>();
        var toplevels = _services.Require<IToplevelModel>();
        toplevelSource.MinimizedGeometryRequested += (window, panel, box) =>
        {
            if (box.IsEmpty || panel is null)
            {
                toplevelSource.SetMinimizedGeometry(window, default);
                return;
            }

            var origin = _sceneSurfaces.TryGetValue(panel, out var panelScene)
                ? panelScene.Tree.ScenePosition
                : (X: 0, Y: 0);
            toplevelSource.SetMinimizedGeometry(
                window, new Box(origin.X + box.X, origin.Y + box.Y, box.Width, box.Height));
        };
        _services.Require<XdgDialogManager>().ModalChanged += (window, modal) =>
        {
            if (_windows.ViewFor(window.Surface) is { } view)
            {
                _animations.SetModal(view, modal);
            }
        };
        _animations.WindowGeometry = view => new Box(
            view.Tree.ScenePosition.X, view.Tree.ScenePosition.Y, view.GeometrySize().Width, view.GeometrySize().Height);
        _animations.ParentTop = view => view.Xdg.Parent is { } parent && _windows.ViewFor(parent.Surface) is { } owner
            ? owner.Tree.ScenePosition.Y
            : view.Tree.ScenePosition.Y;
        _animations.IconGeometry = view =>
            toplevelSource.TryGet(toplevelSource.IdFor(view.Xdg), out var info) ? info.MinimizedGeometry : default;
        _animations.IconEdge = view =>
        {
            var icon = _animations.IconGeometry?.Invoke(view) ?? default;
            if (icon.IsEmpty)
            {
                return MinimizeEdge.Bottom;
            }

            var screen = _layout.BoxOf(_layout.OutputAt(icon.X, icon.Y) ?? _outputs.Views[0].Output);
            if (icon.Width >= icon.Height)
            {
                return icon.Y + (icon.Height / 2) <= screen.Y + (screen.Height / 2)
                    ? MinimizeEdge.Top
                    : MinimizeEdge.Bottom;
            }

            return icon.X + (icon.Width / 2) <= screen.X + (screen.Width / 2)
                ? MinimizeEdge.Left
                : MinimizeEdge.Right;
        };
        _animations.CursorPosition = () => _seat is { } pointer ? (pointer.PointerX, pointer.PointerY) : (0, 0);
        _animations.MinimizeSettled += view =>
        {
            if (view.Minimized && !view.Tree.IsDestroyed)
            {
                view.Tree.Enabled = false;
            }
        };
        _windows.MinimizeAnimation = (view, minimized) => _animations.OnMinimize(view, minimized);
        _windows.MaximizeRequested = (view, from, current) =>
            _animations.OnMaximizeRequested(view, from, current);
        _windows.MaximizeAnimation = (view, from, to) => _animations.OnMaximized(view, from, to);
        _windows.FullscreenStretchRequested = (view, from, current) =>
            _animations.OnFullscreenRequested(view, from, current);
        _windows.FullscreenAnimation = (view, from, to) => _animations.OnFullscreened(view, from, to);
        _animations.WindowFrame = view => _windows.LocalFrame(view);
        if (_effectsConfig.IsEnabled("diminactive", false))
        {
            _dimShader = _renderer.CompilePixelShader(DimShader.Source, DimShader.Uniforms);
            _dim = new DimInactiveEffect(
                _dimShader,
                new DimInactiveOptions { Strength = _effectsConfig.Integer("diminactive", "Strength", 25) });
            _dim.FadeTo(1.0, default, AnimationDuration.Zero);
            _windows.FocusChanged = ApplyDim;
            _windows.ViewMapped += _ => ApplyDim();
            _windows.ViewRemoved += _ => ApplyDim();
        }

        _windows.ViewMapped += view =>
        {
            if (_animations.OnMapped(view))
            {
                _outputs.ScheduleAll();
            }
        };
        _windows.ViewRemoved += view =>
        {
            if (_animations.OnClosing(view, _placement.Layers.Windows))
            {
                _outputs.ScheduleAll();
            }

            _animations.Forget(view);
        };
        _desktops.Toplevels = toplevels;
        capturePack.Attach(toplevels, surface =>
            _windows.ViewFor(surface) is { } view
                ? new ToplevelCaptureTrees(view.Scene.Tree, null)
                : null);
        var appMenus = _services.Require<AppMenuManager>();
        appMenus.AddressChanged += (surface, service, path) =>
        {
            if (_windows.ViewFor(surface) is { } view)
            {
                toplevelSource.SetAppMenu(view.Xdg, service, path);
            }
        };
        _windows.ViewMapped += view =>
        {
            if (appMenus.MenuOf(view.Surface) is { } menu)
            {
                toplevelSource.SetAppMenu(view.Xdg, menu.ServiceName, menu.ObjectPath);
            }
        };
        _outputs.LayoutChanged += _windows.ArrangeLayerSurfaces;
        _outputs.LayoutChanged += _windows.ClampIntoLayout;
        _services.Require<ScreenEdgeManager>().Changed += _windows.ArrangeLayerSurfaces;
        _outputs.Arrange = ArrangeOutputs;
        _outputs.Removed += view => _placedOutputs.Remove(view.Output);
        if (_services.Find<IOutputConfiguration>() is { } configuration)
        {
            configuration.Applied += entries =>
            {
                var positioned = false;
                foreach (var entry in entries)
                {
                    positioned |= entry.Position is not null && entry.Enabled;
                }

                if (positioned)
                {
                    foreach (var entry in entries)
                    {
                        if (entry.Enabled)
                        {
                            _placedOutputs.Add(entry.Output);
                        }
                    }
                }

                _outputs.Relayout();
                PersistKdeOutputSettings();
            };
        }

        _seat = new PlasmaHostSeat(
            _host, _services, _windows, _outputs, _scene, _layout, cursorTheme, inputSink, _uiSurfaces, Stop);

        _seat.Binding = OnBinding;
        if (_feedback is { } feedback)
        {
            feedback.Cursor = _seat.CursorController;
            _seat.TouchBegan = feedback.TouchDown;
            _seat.TouchMoved = feedback.TouchMotion;
            _seat.TouchEnded = feedback.TouchUp;
        }

        _seat.OverlayMotion = OverlayMotion;
        _seat.OverlayButton = OverlayButton;
        _pointerRefresh = new PointerRefresh(_scene, _host.Loop, _seat.RefreshPointer);
        _coreSeat = seat;
        _sessionLock = _services.Require<Basin.Desktop.SessionLockManager>();
        _lockOverlays = _services.Require<ILockOverlaySurfaces>();
        _windows.FocusLocked = () => _sessionLock.IsLocked || _commandLocked;
        _windows.ViewMapped += view =>
        {
            if (_sessionLock.IsLocked)
            {
                RaiseOverlay(view.Surface, view.Tree.X, view.Tree.Y);
            }
        };
        WireSessionLock();

        _outputs.CreateInitialOutputs();
        WireColor();
        if (LoadKdeOutputSettings() is { } kdeOutputs)
        {
            _kdeOutputs = kdeOutputs;
            ApplyKdeOutputSettings();
            RedescribeOutputs();
            _outputs.Added += _ =>
            {
                ApplyKdeOutputSettings();
                RedescribeOutputs();
            };
        }

        if (_options.Backend == BackendKind.Drm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("no connected output");
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += Stop;
        }

        _notify = new KdeConfigNotify(_host.Loop);
        _notify.Changed += ReloadTheme;
    }

    private const int DesktopCount = 4;

    private static readonly Xkb.XkbKeysym LeftKey = Xkb.XkbKeysym.FromName("Left");
    private static readonly Xkb.XkbKeysym RightKey = Xkb.XkbKeysym.FromName("Right");
    private static readonly Xkb.XkbKeysym WKey = Xkb.XkbKeysym.FromName("w");
    private static readonly Xkb.XkbKeysym GKey = Xkb.XkbKeysym.FromName("g");
    private static readonly Xkb.XkbKeysym F7Key = Xkb.XkbKeysym.FromName("F7");
    private static readonly Xkb.XkbKeysym F9Key = Xkb.XkbKeysym.FromName("F9");
    private static readonly Xkb.XkbKeysym F10Key = Xkb.XkbKeysym.FromName("F10");
    private static readonly Xkb.XkbKeysym TKey = Xkb.XkbKeysym.FromName("t");

    private static readonly Xkb.XkbKeysym EqualKey = Xkb.XkbKeysym.FromName("equal");

    private static readonly Xkb.XkbKeysym MinusKey = Xkb.XkbKeysym.FromName("minus");

    private static readonly Xkb.XkbKeysym ZeroKey = Xkb.XkbKeysym.FromName("0");

    internal void Stop() => _loop.Stop();

    private bool StepShadows(in FrameTick tick)
    {
        var animating = false;
        foreach (var window in _windows.Views)
        {
            if (window.Shadow is { } shadow)
            {
                animating |= shadow.Step(tick);
            }
        }

        return animating;
    }

    private bool OnBinding(Xkb.XkbKeysym symbol)
    {
        var meta = _seat.ModActive("Mod4");
        if (meta && _seat.ModActive("Control"))
        {
            if (symbol == LeftKey)
            {
                _desktops.Step(-1);
                _osd.Announce();
                return true;
            }

            if (symbol == RightKey)
            {
                _desktops.Step(1);
                _osd.Announce();
                return true;
            }
        }

        if (meta && StageBinding(symbol))
        {
            _outputs.ScheduleAll();
            return true;
        }

        if (meta && symbol == TKey)
        {
            _tiles.Toggle();
            return true;
        }

        if (meta && ShellModeFor(symbol) is { } mode)
        {
            _overview.Toggle(mode);
            return true;
        }

        return _tiles.Key(symbol) || _overview.Key(symbol);
    }

    public void Ring(Surface? surface) => _feedback?.Ring(surface);

    private void ApplyDim()
    {
        if (_dim is not { } dim)
        {
            return;
        }

        var focused = _windows.FocusedView;
        foreach (var view in _windows.Views)
        {
            if (view.Scene.IsDestroyed)
            {
                continue;
            }

            view.Scene.Content.TextureShader = ReferenceEquals(view, focused) ? null : dim.Shader;
        }

        _outputs.ScheduleAll();
    }

    private bool OverlayMotion(double x, double y)
    {
        if (_feedback is { } feedback)
        {
            feedback.MarkChordHeld = _seat.ModActive("Shift") && _seat.ModActive("Mod4") && !_seat.ModActive("Control");
            feedback.ArrowChordHeld =
                _seat.ModActive("Shift") && _seat.ModActive("Mod4") && _seat.ModActive("Control");
            feedback.Track?.SetHeld(_seat.ModActive("Mod4") && _seat.ModActive("Control"));
            feedback.PointerMoved(x, y, buttonsDown: false);
        }

        return _tiles.PointerMotion(x, y) || _overview.PointerMotion(x, y);
    }

    private bool OverlayButton(double x, double y, uint button, bool pressed)
    {
        _feedback?.PointerButton(x, y, button, pressed);
        return _tiles.PointerButton(x, y, button, pressed) || _overview.PointerButton(x, y, button, pressed);
    }

    private static PlasmaShellMode? ShellModeFor(Xkb.XkbKeysym symbol)
    {
        if (symbol == WKey)
        {
            return PlasmaShellMode.Overview;
        }

        if (symbol == GKey)
        {
            return PlasmaShellMode.Grid;
        }

        if (symbol == F9Key)
        {
            return PlasmaShellMode.WindowsCurrent;
        }

        if (symbol == F10Key)
        {
            return PlasmaShellMode.WindowsAll;
        }

        return symbol == F7Key ? PlasmaShellMode.WindowsClass : null;
    }

    private KwinOutputSettings? LoadKdeOutputSettings()
    {
        if (string.Equals(_options.Config, "false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_options.Config is { } path)
        {
            if (KwinOutputSettings.TryLoad(path, out var named))
            {
                _kdeOutputsPath = path;
                return named;
            }

            _log.Warn($"{path} is not a kwin output configuration");
            return null;
        }

        _kdeOutputsPath = KwinOutputSettings.DefaultPath();
        if (KwinOutputSettings.Locate() is not { } located)
        {
            _ = KwinOutputSettings.TryLoad(out var fresh);
            return fresh;
        }

        if (KwinOutputSettings.TryLoad(located, out var settings))
        {
            return settings;
        }

        _log.Warn($"{located} is not a kwin output configuration");
        _kdeOutputsPath = null;
        return null;
    }

    private void ApplyKdeOutputSettings()
    {
        if (_kdeOutputs is not { } settings || _services.Find<IOutputConfiguration>() is not { } configuration)
        {
            return;
        }

        var outputs = new List<IOutput>(_outputs.Views.Count);
        foreach (var view in _outputs.Views)
        {
            outputs.Add(view.Output);
        }

        var entries = settings.EntriesFor(outputs);
        if (entries.Count == 0)
        {
            return;
        }

        if (_options.Scales.Length > 0)
        {
            var kept = new List<OutputConfigurationEntry>(entries.Count);
            foreach (var entry in entries)
            {
                kept.Add(entry with { Scale = null });
            }

            entries = kept;
        }

        _kdeOutputsApplying = true;
        var applied = settings.Apply(configuration, entries);
        _kdeOutputsApplying = false;
        if (applied)
        {
            foreach (var entry in entries)
            {
                _log.Info($"{entry.Output.Name} takes the kwin settings: scale {entry.Output.Scale}, mode {($"{entry.Output.CurrentMode.Width}x{entry.Output.CurrentMode.Height}")}");
            }
        }
        else
        {
            _log.Warn($"the kwin output settings did not apply");
        }
    }

    private void ArrangeOutputs(IReadOnlyList<OutputView> views)
    {
        var unplaced = new List<IOutput>(views.Count);
        var edge = 0;
        foreach (var view in views)
        {
            if (_placedOutputs.Contains(view.Output))
            {
                var box = _layout.BoxOf(view.Output);
                edge = Math.Max(edge, box.X + box.Width);
            }
            else
            {
                unplaced.Add(view.Output);
            }
        }

        if (unplaced.Count == views.Count)
        {
            _layout.ArrangeHorizontally(unplaced);
            return;
        }

        foreach (var output in unplaced)
        {
            _layout.Move(output, edge, 0);
            edge += _layout.BoxOf(output).Width;
        }
    }

    private void PersistKdeOutputSettings()
    {
        if (_kdeOutputsApplying || _kdeOutputs is not { } settings || _kdeOutputsPath is not { } path ||
            _services.Find<IOutputConfiguration>() is not { } configuration)
        {
            return;
        }

        var outputs = new List<IOutput>(_outputs.Views.Count);
        foreach (var view in _outputs.Views)
        {
            outputs.Add(view.Output);
        }

        if (outputs.Count == 0)
        {
            return;
        }

        IReadOnlyList<OutputConfigurationEntry> entries =
            KwinOutputSettings.Snapshot(outputs, configuration, _layout, _services.Find<IOutputOrder>());
        if (_options.Scales.Length > 0)
        {
            var kept = new List<OutputConfigurationEntry>(entries.Count);
            foreach (var entry in entries)
            {
                kept.Add(entry with { Scale = null });
            }

            entries = kept;
        }

        if (settings.Record(entries) && settings.Save(path))
        {
            _log.Debug($"the kwin output settings were written to {path}");
            return;
        }

        _log.Warn($"the kwin output settings did not save to {path}");
    }

    private static BlurOptions ReadBlurOptions()
    {
        var path = KdeIni.ConfigPath("kwinrc");
        var defaults = new BlurOptions();
        return new BlurOptions
        {
            Strength = int.TryParse(KdeIni.ReadEntry(path, "Effect-blur", "BlurStrength"), out var strength)
                ? strength
                : defaults.Strength,
            NoiseStrength = int.TryParse(KdeIni.ReadEntry(path, "Effect-blur", "NoiseStrength"), out var noise)
                ? noise
                : defaults.NoiseStrength,
            Saturation = int.TryParse(KdeIni.ReadEntry(path, "Effect-blur", "Saturation"), out var saturation)
                ? saturation / 100.0
                : defaults.Saturation,
        };
    }

    private Box ScreenAreaOf(SceneSurface scene)
    {
        var (x, y) = scene.Tree.ScenePosition;
        var output = _layout.OutputAt(x, y);
        return output is null ? _layout.Bounds : _layout.BoxOf(output);
    }

    private void WireSessionLock()
    {
        _lockDriver = new Basin.Desktop.SessionLockSceneDriver(
            _sessionLock, _coreSeat, _placement.Layers.Lock, _layout, _placement.Layers.SetLocked);
        _sessionLock.Locked += () => _commandLocked = false;
        _lockDriver.Locked += () =>
        {
            foreach (var view in _windows.Views)
            {
                RaiseOverlay(view.Surface, view.Tree.X, view.Tree.Y);
            }

            foreach (var (layer, scene) in _windows.LayerSurfaces)
            {
                if (scene is not null)
                {
                    RaiseOverlay(layer.Surface, scene.Tree.X, scene.Tree.Y);
                }
            }

            foreach (var shell in _manager.Surfaces)
            {
                if (_placement.SceneOf(shell) is { } scene)
                {
                    RaiseOverlay(shell.Surface, scene.Tree.X, scene.Tree.Y);
                }
            }

            BasinReport.Line($"LOCKED");
        };
        _lockDriver.Unlocked += () =>
        {
            foreach (var scene in _overlayScenes)
            {
                if (!scene.IsDestroyed)
                {
                    scene.Destroy();
                }
            }

            _overlayScenes.Clear();
            if (_windows.Views.Count > 0)
            {
                _windows.Focus(_windows.Views[0]);
            }

            BasinReport.Line($"UNLOCKED");
        };
        _lockDriver.Abandoned += () => BasinReport.Line($"LOCK ABANDONED (staying blanked)");
        _lockDriver.LockSurfaceAdded += (_, _) =>
        {
            foreach (var overlay in _overlayScenes)
            {
                if (!overlay.IsDestroyed)
                {
                    overlay.Tree.RaiseToTop();
                }
            }
        };
    }

    private void LockNow()
    {
        if (_sessionLock.IsLocked || _commandLocked)
        {
            return;
        }

        _commandLocked = true;
        _lockDriver.LockNow();
    }

    private void UnlockNow()
    {
        if (_sessionLock.IsLocked || !_commandLocked)
        {
            return;
        }

        _commandLocked = false;
        _lockDriver.UnlockNow();
    }

    private void RaiseOverlay(Surface surface, int x, int y)
    {
        if (!surface.IsMapped || !_lockOverlays.IsAllowed(surface))
        {
            return;
        }

        foreach (var existing in _overlayScenes)
        {
            if (!existing.IsDestroyed && ReferenceEquals(existing.Surface, surface))
            {
                return;
            }
        }

        var scene = new SceneSurface(_placement.Layers.Lock, surface);
        scene.Tree.SetPosition(x, y);
        _overlayScenes.Add(scene);
    }

    private void OnOffloadCandidates(OutputView view, IReadOnlyList<SceneBuffer> candidates)
    {
        if (_dmabufGlobal is null ||
            view.Output is not Basin.Backend.Drm.DrmOutput drmOutput ||
            drmOutput.OverlayScanoutFormats.Count == 0)
        {
            return;
        }

        _offloadFeedbackGone.Clear();
        foreach (var surface in _offloadFeedback)
        {
            var still = false;
            for (var i = 0; i < candidates.Count && !still; i++)
            {
                still = candidates[i].InputSurface == surface;
            }

            if (!still)
            {
                _offloadFeedbackGone.Add(surface);
            }
        }

        foreach (var surface in _offloadFeedbackGone)
        {
            _offloadFeedback.Remove(surface);
            _dmabufGlobal.SetScanoutTargets(surface, null);
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].InputSurface is { } surface && _offloadFeedback.Add(surface))
            {
                _dmabufGlobal.SetScanoutTargets(surface, drmOutput.OverlayScanoutFormats);
            }
        }
    }

    private int RunLoop()
    {
        BasinReport.Line(CompositorLines.Socket(_host.Socket));

        _stdin = new StdinCommands(_host.Loop, HandleCommand);
        _stdin.CommandFailed += (command, error) =>
        {
            BasinReport.Line(CompositorLines.CommandFailed(command, error));
        };

        _seat.CenterCursor();
        _uiDriver.Woken += _outputs.ScheduleAll;
        _uiDriver.Start();
        StartShell();

        _loop.Iterating += _uiDriver.Pump;
        _loop.Frames = _options.Frames;
        _loop.Run();
        _loop.Iterating -= _uiDriver.Pump;

        _uiDriver.Woken -= _outputs.ScheduleAll;
        _stdin.Stop();
        return 0;
    }

    private void StartShell()
    {
        if (_options.Shell is not { Length: > 0 } shell)
        {
            return;
        }

        try
        {
            _shell = BasinDiagnostics.StartClient(
                shell, _host.Socket, [("QT_QPA_PLATFORM", "wayland"), ("DISPLAY", null)]);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException)
        {
            _log.Error($"could not start {shell}: {error.Message}");
        }
    }

    private void HandleCommand(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (_seat.StdinCommands.Handle(parts))
        {
            return;
        }

        switch (parts)
        {
            case ["shot", var path]:
                WriteScreenshot(path);
                break;
            case ["shot", var path, var outputName]:
                WriteScreenshot(path, outputName);
                break;
            case ["shotraw", var path]:
                DumpPresented(path);
                break;
            case ["shotraw", var path, var outputName]:
                DumpPresented(path, outputName);
                break;
            case ["lock"]:
                LockNow();
                break;
            case ["unlock"]:
                UnlockNow();
                break;
            case ["where"]:
                PrintState();
                break;
            case ["zoom", var factor]:
                _stages.Zoom?.ZoomTo(double.Parse(factor));
                _outputs.ScheduleAll();
                break;
            case ["magnify", var factor]:
                if (_stages.Magnifier is { } lens)
                {
                    lens.TargetZoom = double.Parse(factor);
                    _outputs.ScheduleAll();
                }

                break;
            case ["blend"]:
                BlendChanges();
                break;
            case ["maximize", var state]:
                if (_windows.FocusedView is { } target)
                {
                    _windows.SetMaximized(target, state == "1");
                    _outputs.ScheduleAll();
                }

                break;
            case ["offload", ("on" or "off" or "overlay") and var mode]:
                _outputs.AllowPlaneOffload = mode != "off";
                _outputs.AllowDirectScanout = mode == "on";
                foreach (var view in _outputs.Views)
                {
                    view.Scene?.Ring.AddWhole();
                }

                _outputs.ScheduleAll();
                BasinReport.Line($"OFFLOAD {mode}");
                break;
            case ["quit"]:
                Stop();
                break;
        }
    }

    private bool StageBinding(Xkb.XkbKeysym symbol)
    {
        if (symbol != EqualKey && symbol != MinusKey && symbol != ZeroKey)
        {
            return false;
        }

        if (_seat.ModActive("Control") && _stages.Magnifier is { } lens)
        {
            if (symbol == EqualKey)
            {
                lens.ZoomIn();
            }
            else if (symbol == MinusKey)
            {
                lens.ZoomOut();
            }
            else
            {
                lens.Reset();
            }

            return true;
        }

        if (_stages.Zoom is not { } zoom)
        {
            return false;
        }

        if (symbol == EqualKey)
        {
            zoom.ZoomIn();
        }
        else if (symbol == MinusKey)
        {
            zoom.ZoomOut();
        }
        else
        {
            zoom.Reset();
        }

        return true;
    }

    private void BlendChanges()
    {
        if (!_stages.BlendsChanges)
        {
            return;
        }

        var duration = _effectsConfig.Duration(BlendChangesStage.DefaultMillis);
        foreach (var view in _outputs.Views)
        {
            if (view.Scene is not { } scene || _stages.BlendFor(scene) is not { } blend)
            {
                continue;
            }

            var mode = view.Output.CurrentMode;
            var box = _layout.BoxOf(view.Output);
            var capture = new MemoryBuffer(mode.Width, mode.Height, DrmFormat.Xrgb8888);
            var options = new SceneRenderOptions
            {
                Background = new RenderColor(0f, 0f, 0f, 1f),
                Projection = OutputProjection.For(view.Output),
                OriginX = box.X,
                OriginY = box.Y,
            };
            if (_scene.Render(_renderer, capture, options))
            {
                blend.Begin(
                    capture, new FrameTick(view.Scheduler?.PredictedVblankNanos ?? 0, 0), duration);
            }

            capture.Destroy();
        }

        _outputs.ScheduleAll();
    }

    private void DumpPresented(string path, string? outputName = null)
    {
        if (ViewNamed(outputName) is not { } view)
        {
            BasinReport.Line($"SHOTRAW unavailable (no output)");
            return;
        }

        BasinReport.Line(SceneScreenshot.WritePresented(view.LastPresentedBuffer, _renderer, path) switch
            {
                ScreenshotOutcome.NoFrame => "SHOTRAW unavailable (nothing presented yet)",
                ScreenshotOutcome.Unreadable => "SHOTRAW unavailable (presented buffer not importable)",
                _ => $"SHOTRAW {path}",
            });
    }

    private void PrintState()
    {
        BasinReport.Line($"POINTER {_seat.PointerX} {_seat.PointerY}");
        BasinReport.Line($"STAGES zoom={(_stages.Zoom is { } z ? z.Zoom.ToString("0.###") : "off")} " + $"grid={(_stages.Zoom?.DrawsPixelGrid == true ? "yes" : "no")} " + $"magnifier={(_stages.Magnifier is { } m ? m.Zoom.ToString("0.###") : "off")} " + $"invert={(_stages.Invert is null ? "off" : "on")} " + $"colorblind={(_stages.ColorBlindness is null ? "off" : "on")} " + $"showpaint={(_stages.ShowPaint is null ? "off" : "on")} " + $"blend={(_stages.IsBlending ? "running" : "idle")} " + $"transform={(_stages.ScreenTransform?.IsRunning == true ? "running" : "idle")} " + $"backdrops={_backdrops.AttachedCount}/{_backdrops.BindingCount}");
        foreach (var view in _outputs.Views)
        {
            var box = _layout.BoxOf(view.Output);
            BasinReport.Line($"AREA {view.Output.Name} output={box} usable={_windows.UsableArea(view.Output)}");
        }

        foreach (var shell in _manager.Surfaces)
        {
            if (!shell.Surface.IsMapped)
            {
                continue;
            }

            var scene = _placement.SceneOf(shell);
            var current = shell.Surface.Current;
            var box = scene is null
                ? new Box(0, 0, current.Width, current.Height)
                : new Box(scene.Tree.X, scene.Tree.Y, current.Width, current.Height);
            var layer = PlasmaShellPlacement.LayerOf(shell.Role) is { } kind
                ? kind.ToString().ToLowerInvariant()
                : "windows";
            BasinReport.Line($"SURFACE role={RoleName(shell.Role)} layer={layer} box={box} " + $"hidden={(shell.IsAutoHidden ? "yes" : "no")} focusable={(shell.Focusable ? "yes" : "no")}");
        }

        foreach (var view in _windows.Views)
        {
            var (width, height) = view.GeometrySize();
            var palette = _services.Require<ServerDecorationPaletteManager>().PaletteOf(view.Surface)?.Palette ?? "";
            BasinReport.Line($"WINDOW \"{view.Xdg.Title}\" box={new Box(view.Tree.X, view.Tree.Y, width, height)} " + $"focused={(ReferenceEquals(view, _windows.FocusedView) ? "yes" : "no")} " + $"maximized={(view.Maximized ? "yes" : "no")} palette=\"{palette}\"");
        }

        foreach (var (layer, scene) in _windows.LayerSurfaces)
        {
            if (scene is null)
            {
                continue;
            }

            var current = layer.Surface.Current;
            BasinReport.Line($"LAYER ns={layer.Namespace} layer={layer.Layer.ToString().ToLowerInvariant()} " + $"box={new Box(scene.Tree.X, scene.Tree.Y, current.Width, current.Height)}");
        }

    }

    private static string RoleName(PlasmaShellRole role) => role switch
    {
        PlasmaShellRole.OnScreenDisplay => "onscreendisplay",
        PlasmaShellRole.CriticalNotification => "criticalnotification",
        PlasmaShellRole.AppletPopup => "appletpopup",
        _ => role.ToString().ToLowerInvariant(),
    };

    private void ReloadTheme()
    {
        if (BreezePalette.Load(null) == _theme.Default)
        {
            return;
        }

        BlendChanges();
        _theme.Reload();
        _log.Debug($"colour scheme reloaded, the chrome is {_theme.Variant.ToString().ToLowerInvariant()}");
        _ui.Theme = _theme.Variant;
        _osd.RefreshTheme();
        _overview.RefreshTheme();
        _tiles.RefreshTheme();
        foreach (var view in _windows.Views)
        {
            _windows.LayoutDecorations(view);
        }

        _outputs.ScheduleAll();
    }

    private void WriteScreenshot(string? screenshotPath, string? outputName = null)
    {
        if (screenshotPath is not { Length: > 0 } path || ViewNamed(outputName) is not { } view)
        {
            return;
        }

        var mode = view.Output.CurrentMode;
        var box = _layout.BoxOf(view.Output);
        var cursor = default(CursorBlit);
        if (_seat.CursorSprite is { } sprite)
        {
            var scale = view.Output.Scale;
            cursor = new CursorBlit(sprite.Buffer, new Box(
                (int)((_seat.PointerX - box.X) * scale) - sprite.HotspotX,
                (int)((_seat.PointerY - box.Y) * scale) - sprite.HotspotY,
                sprite.Buffer.Width,
                sprite.Buffer.Height));
        }

        var options = new SceneRenderOptions
        {
            Background = new RenderColor(0f, 0f, 0f, 1f),
            Projection = OutputProjection.For(view.Output),
            OriginX = box.X,
            OriginY = box.Y,
        };
        if (SceneScreenshot.Write(_scene, _renderer, path, mode.Width, mode.Height, options, cursor))
        {
            BasinReport.Line($"SHOT {path} {mode.Width}x{mode.Height}");
        }
    }

    private OutputView? ViewNamed(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return _outputs.Views.FirstOrDefault();
        }

        foreach (var view in _outputs.Views)
        {
            if (string.Equals(view.Output.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return view;
            }
        }

        return null;
    }

    public void Dispose()
    {
        BasinDiagnostics.StopClient(_shell);
        _notify.Dispose();
        _screencast?.Dispose();
        _uiDriver.Dispose();
        _tiles.Dispose();
        _overview.Dispose();
        _osd.Dispose();
        _frames.Dispose();
        _ui.Dispose();
        _gpu?.Dispose();
        _screens.Dispose();
        _outputs.Dispose();
        _virtualBackend.Dispose();
        _pointerRefresh?.Dispose();
        _scene.Root.Destroy();
        _seat.Dispose();
        _feedback?.Dispose();
        _dimShader?.Dispose();
        _stages.Dispose();
        _backdrops.Dispose();
        _sceneSurfaces.Clear();
        _services.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
