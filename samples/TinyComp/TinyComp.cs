using System.Diagnostics;
using Basin;
using Basin.Host;
using Basin.Backend.Libinput;
using Basin.Cli;
using Basin.Effects;
using Basin.Backend.Wayland;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Capabilities;
using Basin.UI.Skia;
using Wayland;
using Wayland.Server;

using Basin.Diagnostics;

namespace TinyComp;

internal sealed partial class TinyComp :
    IDisposable,
    Basin.Seat.ITouchChrome,
    Basin.Seat.ITouchActivitySink,
    Basin.Seat.ITouchPointerTarget,
    Basin.Seat.ITouchDragHandler,
    Basin.Seat.ICentroidSwipeHandler
{
    private readonly Basin.Host.BasinHost _host;
    private readonly WlServerDisplay _display;
    private readonly WaylandEventLoop _loop;
    private StdinCommands? _stdinCommands;
    internal WaylandEventLoop Loop => _loop;
    private readonly WaylandBackend? _backend;
    private NestedSeam? _seam;
    private WaylandSeamTextInput? _seamTextInput;
    private readonly List<SurfaceBox> _caretSurfaces = [];
    private readonly Basin.Session.ISession? _session;
    private readonly Basin.Backend.Drm.DrmBackend? _drm;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private readonly Basin.Backend.Libinput.LibinputTabletSource? _tablets;
    private readonly Basin.Capabilities.ISessionStore _sessionStore;
    private readonly Dictionary<XdgToplevelWindow, (string Session, string Name)> _sessionWindows = [];
    private readonly LayoutPointer? _pointer;
    private readonly Basin.Desktop.CursorController _cursor;
    private readonly CompositorGlobal _compositor;
    private readonly Basin.Seat.Seat _seat;
    private readonly XdgShell _shell;
    private readonly IRenderer _renderer;
    private readonly IAllocator? _allocator;
    private readonly Basin.Host.OutputDriver _driver;
    private readonly Scene _scene = new();
    private PointerRefresh? _pointerRefresh;
    private SceneLayers _layers = null!;
    private readonly EffectsPolicy _effects;
    private PostEffects _post = null!;
    private ScreenShader _shader = null!;
    private FeedbackEffects? _feedback;
    private IPixelShader? _dimShader;
    private bool _dimShaderTried;
    private Basin.Effects.DropShadowTexture? _shadowTexture;
    private LayerShell _layerShell = null!;
    private Basin.Desktop.SessionLockManager _sessionLock = null!;
    private Basin.Desktop.SessionLockSceneDriver _lockDriver = null!;
    private readonly XdgDecorationManager _decorations;
    private readonly XdgToplevelDragManager _toplevelDrags;
    private readonly Basin.Desktop.KdeServerDecorationManager _kdeDecorations;
    private readonly Dictionary<Surface, bool> _ssdPreference = [];
    private Basin.Capabilities.IUIHost? _uiHost;
    private FrameStyle _frameStyle;
    private FrameTheme? _frameTheme;

    internal IFrameRenderer? CreateFrameRenderer() => CreateFrameRenderer(_frameStyle);

    internal IFrameRenderer? CreateFrameRenderer(FrameStyle style) => style switch
    {
        FrameStyle.Beos => new BeosFrameRenderer(_frameTheme ??= new FrameTheme()),
        FrameStyle.Flat => new SkiaFrameRenderer(_frameTheme ??= new FrameTheme()),
        _ => null,
    };

    internal Basin.Capabilities.IUIHost UIHost => _uiHost ??= SkiaUIHosts.For(_renderer);
    private readonly BasinServices _services;
    private readonly SceneScreenCapture _capture;
    private readonly SceneDmabufCapture _dmabufCapture;
    private readonly ToplevelSceneIndex _captureIndex;
    private readonly SceneToplevelStack _stack;
    private readonly Basin.XWayland.XWaylandModule _xwaylandModule;
    private readonly Basin.Capabilities.Defaults.CursorImageTheme _cursorTheme;
    private readonly Basin.Backend.Drm.DrmOutputGamma _gamma;
    private readonly XdgToplevelSource _xdgToplevels;
    private readonly Basin.Capabilities.IToplevelModel _toplevels;
    private readonly Basin.Seat.SeatIdleSource? _idleSource;
    private Basin.Desktop.CursorShapeManager _cursorShapes = null!;
    private Basin.Desktop.ScreencopyManager _screencopy = null!;
    private Basin.Desktop.RelativePointerManager _relativePointer = null!;
    private Basin.Desktop.PointerConstraintsManager _constraints = null!;
    private Basin.Desktop.ForeignToplevelListManager _toplevelList = null!;
    private readonly WorkspacePolicy _workspaceModel;
    private Basin.Desktop.PointerGesturesManager _gestures = null!;
    private Basin.Desktop.KeyboardShortcutsInhibitManager _shortcutsInhibit = null!;
    private Basin.Desktop.TextInputManager _textInput = null!;
    private Basin.Desktop.ColorManager _color = null!;
    private Basin.XWayland.XwaylandShellGlobal _xwaylandShell = null!;
    private Basin.XWayland.XWaylandServer? _xwayland;
    private Basin.XWayland.XWaylandWm? _xwm;
    private readonly List<XWindow> _xwindows = [];
    private readonly Basin.XWayland.XWaylandSceneDriver _xSceneDriver = new();
    private Basin.Desktop.TearingControlManager _tearing = null!;
    private Basin.Desktop.ContentTypeManager _contentType = null!;
    private IOutputConfiguration? _outputConfiguration;
    private Basin.Color.ColorOutputConfiguration? _colorConfiguration;
    private Basin.Color.ColorCapabilityPack _colorPack = null!;
    private Basin.Desktop.OrientationSensor? _orientationSensor;
    private Basin.Capabilities.Defaults.LayoutOutputConfiguration? _layoutConfiguration;
    private Basin.Desktop.FifoManager _fifo = null!;
    private Basin.Capabilities.IFrameClock _frameClock = null!;
    private Basin.Desktop.IdleManager _idle = null!;
    private Basin.Desktop.LayerShellSceneDriver _layerDriver = null!;
    private readonly Basin.Desktop.PopupPlacer _popupPlacer;
    private readonly OutputLayout _layout = new();
    private readonly List<HostChrome> _hostChrome = [];
    private bool _probed;
    private bool _outputsCreated;
    private readonly List<Basin.Backend.Drm.DrmBackend> _secondaryBackends = [];
    private readonly List<IAllocator> _secondaryAllocators = [];
    private readonly List<Basin.Render.Vulkan.VulkanDeviceBlitter> _blitters = [];

    private IBackdropEffect? _blurEffect;
    private IPixelShader? _fireShader;
    private int _cornerRadius;
    private Basin.Desktop.BackgroundEffectManager _backgroundEffects = null!;
    private CrossDeviceImportCache? _blitCache;
    private readonly List<Window> _windows = [];
    private readonly string _socket;
    private Basin.Seat.Backends.DragIconFollower _dragIcon = null!;
    private Window? _focused;
    private XWindow? _focusedX;
    private DragMode _mode;
    private IGrabTarget? _grabWindow;
    private double _grabX, _grabY;
    private Box _grabStart;
    private ResizeEdges _grabEdges;
    private double _cursorX, _cursorY;
    private double _lastRawX, _lastRawY;
    private readonly Basin.Host.CompositorRunLoop _runLoop;
    private readonly long _frames;
    private readonly bool _fullRepaint;
    private bool _offload;
    private readonly bool _hdr;
    private readonly Basin.Capabilities.OutputColorProfileSource _colorSource;
    private readonly string? _iccProfile;
    private bool _damageTint;
    private double[] _scales;
    private Basin.Desktop.FractionalScaleManager _fractionalScale = null!;
    private Surface? _scanoutFeedbackSurface;
    private LinuxDmabufGlobal? _dmabufGlobal;
    private readonly IDisposable? _protocolTrace;

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);

    private const int CornerZone = 24;

    private const int SplitGrabZone = 8;

    private enum DragMode
    {
        None,
        Move,
        Resize,
        Split,
    }

    private readonly string? _channelEndpoint;
    private Basin.Transport.Waypipe.WaypipeChannel? _channel;
    private Config _config;
    private readonly string? _configPath;
    private string _rendererName = "vulkan";
    private bool _cornerShaderTried;
    private bool _fireShaderTried;

    private readonly Dictionary<int, IPixelShader> _cornerShaders = [];

    private IPixelShader? CornerShaderFor(int radius)
    {
        if (radius <= 0)
        {
            return null;
        }

        if (_cornerShaders.TryGetValue(radius, out var cached))
        {
            return cached;
        }

        if (_cornerShaderTried && _cornerShaders.Count == 0)
        {
            return null;
        }

        _cornerShaderTried = true;
        var shader = _renderer.CompilePixelShader(CornerShader.Source, CornerShader.Uniforms);
        if (shader is null)
        {
            _log.Warn($"{_rendererName} compiles no pixel shader dialect; corner_radius is ignored");
            return null;
        }

        var cornerScale = _scales.Length > 0 ? _scales[0] : 1.0;
        shader.SetUniforms([(float)(radius * cornerScale)]);
        _cornerShaders[radius] = shader;
        return shader;
    }

    private void ApplyEffectShaders()
    {
        var fire = _effects.CloseKind == "fire-gpu"
            || _config.Rules.Any(static rule => rule.Close == "fire-gpu");
        _effects.FireShaderHandle = fire ? FireShaderHandle() : null;
    }

    private void ApplyCornerRadius(int radius) =>
        _cornerRadius = radius > 0 && CornerShaderFor(radius) is not null ? radius : 0;

    private IPixelShader? FireShaderHandle()
    {
        if (_fireShaderTried)
        {
            return _fireShader;
        }

        _fireShaderTried = true;
        _fireShader = _renderer.CompilePixelShader(Basin.Effects.FireShader.Source, Basin.Effects.FireShader.Uniforms);
        if (_fireShader is null)
        {
            _log.Warn($"{_rendererName} compiles no pixel shader dialect; fire-gpu falls back to the particle mesh");
        }

        return _fireShader;
    }

    public TinyComp(Config config, BackendKind backend = BackendKind.Nested, int socketFd = -1, BasinLogger log = default, bool managedTransport = false, string? channelEndpoint = null, string? configPath = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var drm = backend == BackendKind.Drm;
        _config = config;
        _configPath = configPath;
        var outputCount = config.Outputs;
        var rendererName = config.Renderer;
        _log = log;
        _iccProfile = config.IccProfile;
        _frames = config.Frames;
        _effects = new EffectsPolicy();
        _useTransactions = config.Transactions;
        _frameStyle = config.FrameStyle;
        _fullRepaint = config.FullRepaint;
        _offload = config.Offload;
        _nightLightKelvin = config.NightLight;
        _hdr = config.Hdr;
        _colorSource = config.ColorSource;
        _damageTint = config.DamageTint;
        _scales = config.Scales;
        _host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(drm ? "drm" : backend == BackendKind.Headless ? "headless" : "nested") with
            {
                Transport = managedTransport || channelEndpoint is not null
                    ? Basin.Host.HostTransport.Managed
                    : Basin.Host.HostTransport.LibWayland,
                SocketFd = socketFd,
            });
        _channelEndpoint = channelEndpoint;
        _display = _host.Display;
        _socket = _host.Socket;
        _runLoop = new Basin.Host.CompositorRunLoop(_host)
        {
            DispatchTimeoutMillis = -1,
            FlushParentFirst = true,
            RenderedFrames = () => Rendered,
        };

        if (TraceEnabled)
        {
            _protocolTrace = Basin.Diagnostics.WaylandDiagnostics.TraceProtocol(_display);
        }

        _loop = _host.Loop;
        _display.ClientCreated += client =>
        {
            if (client.TryGetCredentials(out var credentials))
            {
                _log.Debug($"client connected: pid={credentials.Pid} uid={credentials.Uid}");
            }
            else
            {
                _log.Debug($"client connected: no local process behind it");
            }
        };
        _layers = new SceneLayers(_scene.Root);
        _pointerRefresh = new PointerRefresh(_scene, _loop, RefreshPointer);

        var forcedCard = Environment.GetEnvironmentVariable("BASIN_DRM_DEVICE");
        IReadOnlyList<DrmDeviceInfo> drmDevices = [];
        if (drm)
        {
            _session = _host.Session;
            _drm = _host.Drm!;

            _input = new Basin.Backend.Libinput.LibinputBackend(_loop, _session!);
            _tablets = new Basin.Backend.Libinput.LibinputTabletSource(_input);

            drmDevices = DrmDevices.Enumerate();
            foreach (var device in drmDevices)
            {
                if (device.RenderNodePath is { } node && node != _drm.RenderNodePath)
                {
                    try
                    {
                        _blitters.Add(new Basin.Render.Vulkan.VulkanDeviceBlitter(node));
                        BasinReport.Line($"BLIT {node} ({device.Driver})");
                    }
                    catch (Exception e) when (e is InvalidOperationException or DllNotFoundException)
                    {
                        BasinReport.Line($"BLIT {node} unavailable: {e.Message}");
                    }
                }
            }

            if (_blitters.Count > 0)
            {
                _blitCache = new CrossDeviceImportCache(source =>
                {
                    var preferred = source is DmabufBuffer { SamplingDevice: not 0 } dmabuf ? dmabuf.SamplingDevice : 0;
                    if (preferred != 0 && BlitterFor(preferred)?.Convert(source) is { } hinted)
                    {
                        return hinted;
                    }

                    foreach (var blitter in _blitters)
                    {
                        if (blitter.Convert(source) is { } conversion)
                        {
                            return conversion;
                        }
                    }

                    return null;
                });
                _scene.CrossDeviceImport = _blitCache.Get;
            }

            var stack = CreateStack(ref rendererName, _drm.RenderNodePath);
            _renderer = stack.Renderer;

            _allocator = stack.DeviceAllocator;
        }
        else
        {
            _backend = _host.Parent;

            var stack = CreateStack(ref rendererName, Basin.Renderers.RendererCatalog.FindRenderNode());
            _renderer = stack.Renderer;

            _allocator = stack.DeviceAllocator;
        }

        _driver = new Basin.Host.OutputDriver(_host, _scene, _layout, _renderer, _allocator)
        {
            Background = Background,
            Requested = outputCount,
            Scales = config.Scales,
            ContinuousRepaint = _frames > 0,
            FullRepaint = _fullRepaint,
            DebugDamageTint = _damageTint,
            AllowPlaneOffload = _offload,
            NestedName = _ => null,
        };
        WireOutputDriver();

        _popupPlacer = new Basin.Desktop.PopupPlacer(_layout);
        _luts = new Basin.Color.ColorLutCache(_renderer);
        _effects.SlideEnabled = rendererName != "pixman";

        var servicePack = new Basin.Desktop.DesktopServicePack(_scene, _layout, _renderer, _drm);
        var capturePack = servicePack.Capture;
        _capture = capturePack.Capture;
        _dmabufCapture = capturePack.DmabufCapture;
        _captureIndex = capturePack.Index;
        _stack = capturePack.Stack;
        _driver.Capture = capturePack;
        _gamma = servicePack.Drm.Gamma;
        _cursorTheme = servicePack.CursorTheme;
        _orientationSensor = new Basin.Desktop.OrientationSensor(_loop);
        _colorPack = new Basin.Color.ColorCapabilityPack(_layout);
        _layoutConfiguration = _colorPack.Layout;
        _layoutConfiguration.Orientation = _orientationSensor;
        _colorConfiguration = _colorPack.Configuration;
        _colorConfiguration.EdrChanged += output =>
        {
            if (Views.FirstOrDefault(v => v.Output == output) is { } edrView)
            {
                RefreshOutputColor(edrView);
                edrView.Scheduler?.ScheduleRepaint();
            }
        };
        _services = _host.CreateServices()
            .Use(_layout)
            .With(_colorPack)
            .With(servicePack);

        if (_backend is { SupportsTextInput: true } textInputParent)
        {
            _seamTextInput = new WaylandSeamTextInput(textInputParent)
            {
                LocateCursorRectangle = LocateGuestCaret,
            };
            _services.Use<Basin.Capabilities.ITextInputMethod>(_seamTextInput);
        }

        if (_tablets is { } tablets)
        {
            _services.Use<Basin.Capabilities.ITabletSource>(tablets);
        }

        var inputSink = new Basin.Seat.Backends.HookInputSink
        {
            OnPointerMotion = (timeMs, dx, dy) =>
            {
                InjectPointerMotion(timeMs, dx, dy);
                return true;
            },
            OnPointerMotionAbsolute = (timeMs, x, y, extentWidth, extentHeight) =>
            {
                if (extentWidth <= 0 || extentHeight <= 0)
                {
                    return false;
                }

                InjectPointerMotionAbsolute(timeMs, x / extentWidth, y / extentHeight);
                return true;
            },
            OnPointerButton = (timeMs, button, pressed) =>
            {
                InjectPointerButton(timeMs, button, pressed);
                return true;
            },
        };
        inputSink.OnKey = (keyboard, timeMs, keycode, pressed) =>
        {
            _seat!.Keyboard.Activate(keyboard);
            InjectKey(timeMs, keycode, pressed);
            return true;
        };
        _services.Use<Basin.Capabilities.IInputSink>(inputSink);

        _workspaceModel = new WorkspacePolicy(this);
        _services.Use<Basin.Capabilities.IWorkspaceModel>(_workspaceModel);

        if (CreateFrameRenderer() is { } frames)
        {
            _services.Use(frames);
        }

        if (_renderer is Basin.Render.Vulkan.VulkanRenderer vulkanRenderer)
        {
            var vulkanBlur = new VulkanBackdropBlur(vulkanRenderer.Device);
            _blurEffect = vulkanBlur;
            _services.Use<Basin.Capabilities.IBackgroundEffects>(vulkanBlur);
        }
        else if (_renderer is Basin.Render.Gl.GlRenderer glRenderer)
        {
            var glBlur = new GlBackdropBlur(glRenderer.Device);
            _blurEffect = glBlur;
            _services.Use<Basin.Capabilities.IBackgroundEffects>(glBlur);
        }

        _rendererName = rendererName;
        _post = new PostEffects(_renderer, rendererName, _log);
        _post.Configure(config);
        _shader = new ScreenShader(_renderer, rendererName, _allocator, _log);
        _shader.Configure(config);
        ApplyEffectShaders();
        ApplyCornerRadius(config.CornerRadius);

        var xwayland = new Basin.XWayland.XWaylandModule();
        _xwaylandModule = xwayland;
        var desktopPack = Basin.Desktop.DesktopPack.For("tinycomp");

        LinuxDmabufModule? dmabufModule = null;
        if (_renderer.Device is { } dmabufDevice)
        {
            dmabufModule = new LinuxDmabufModule(
                _renderer.DmabufTextureFormats,
                dmabufDevice.DevicePath,
                extraTranches: _blitters.Select(b => (b.DevicePath, b.ImportableFormats)).ToArray());
        }

        _services
            .Install(desktopPack)
            .Install(xwayland);
        if (dmabufModule is not null)
        {
            _services.Install(dmabufModule);
        }

        _services.Freeze();
        _dmabufGlobal = dmabufModule?.Global;

        _sessionStore = _services.Require<Basin.Capabilities.ISessionStore>();
        if (_colorConfiguration is { } wiredColor)
        {
            wiredColor.Brightness = _services.Find<Basin.Capabilities.IOutputBrightness>();
        }

        _compositor = _services.Require<CompositorGlobal>();
        _seat = _services.Require<Basin.Seat.Seat>();
        _grabOrigin = new Basin.Shell.Xdg.GrabOrigin(_seat, () => (_cursorX, _cursorY));
        _shell = _services.Require<XdgShell>();

        _services.Find<Basin.Desktop.LinuxDrmSyncobjManager>()?.DeclareRenderer(_renderer);
        if (_services.Find<IOutputConfiguration>() is { } outputConfiguration)
        {
            _outputConfiguration = outputConfiguration;
            outputConfiguration.Applied += OnOutputConfigurationApplied;
        }

        _xdgToplevels = _services.Require<XdgToplevelSource>();
        _toplevels = _services.Require<Basin.Capabilities.IToplevelModel>();
        capturePack.Attach(_toplevels, surface =>
        {
            SceneNode? content = FindWindow(surface)?.Tree;
            if (content is null)
            {
                foreach (var xwindow in _xwindows)
                {
                    if (xwindow.XWin.Surface == surface)
                    {
                        content = xwindow.Tree;
                        break;
                    }
                }
            }

            return content is null ? null : new ToplevelCaptureTrees(content, null);
        });
        _xdgToplevels.ActivateRequested += toplevel =>
        {
            if (FindWindow(toplevel) is { } window)
            {
                SetMinimized(window, false);
                FocusWindow(window);
            }
        };
        _xdgToplevels.MinimizeRequested += (toplevel, minimized) =>
        {
            if (FindWindow(toplevel) is { } window)
            {
                SetMinimized(window, minimized);
            }
        };
        _xdgToplevels.NoBorderRequested += (toplevel, noBorder) =>
            RecordDecorationPreference(toplevel.Surface, !noBorder);
        _xdgToplevels.CaptureExclusionRequested += (toplevel, excluded) =>
            _xdgToplevels.SetExcludedFromCapture(toplevel, excluded);

        _layerShell = _services.Require<LayerShell>();
        WireLayerShell();
        _toplevelDrags = _services.Require<XdgToplevelDragManager>();
        _decorations = _services.Require<XdgDecorationManager>();
        _decorations.ModeChanged += (toplevel, mode) =>
            RecordDecorationPreference(toplevel.Surface, mode == DecorationMode.ServerSide);
        _sessionLock = _services.Require<Basin.Desktop.SessionLockManager>();
        WireSessionLock();
        _screencopy = _services.Require<Basin.Desktop.ScreencopyManager>();
        _relativePointer = _services.Require<Basin.Desktop.RelativePointerManager>();
        _constraints = _services.Require<Basin.Desktop.PointerConstraintsManager>();
        _constraints.ConstraintCreated += constraint => constraint.Deactivated += () =>
        {
            if (_activeConstraint == constraint)
            {
                _activeConstraint = null;
            }

            SyncParentPointerLock();
        };
        _constraints.ConstraintActivated += constraint =>
        {
            _activeConstraint = constraint;
            SyncParentPointerLock();
        };
        _toplevelList = _services.Require<Basin.Desktop.ForeignToplevelListManager>();
        _gestures = _services.Require<Basin.Desktop.PointerGesturesManager>();
        _shortcutsInhibit = _services.Require<Basin.Desktop.KeyboardShortcutsInhibitManager>();
        _services.Require<Basin.Desktop.VirtualKeyboardManager>().KeymapSubmitted +=
            (fd, _) => fd.Close();
        _services.Require<Basin.Desktop.SystemBellManager>().Rang += _ => BasinReport.Line($"BELL");
        var transientSeats = _services.Require<Basin.Desktop.TransientSeatManager>();
        transientSeats.SeatRequested += request =>
            request.Create(seat => new Basin.Desktop.SceneSeatInput(seat, _scene, _layout));
        transientSeats.SeatCreated += seat => BasinReport.Line($"SEAT {seat.Name}");
        _kdeDecorations = _services.Require<Basin.Desktop.KdeServerDecorationManager>();
        _backgroundEffects = _services.Require<Basin.Desktop.BackgroundEffectManager>();
        _kdeDecorations.ModeRequested += (surface, mode) =>
            RecordDecorationPreference(surface, mode == Basin.Desktop.KdeServerDecorationManager.DecorationMode.Server);
        _services.Require<XdgToplevelIconManager>().IconChanged +=
            (toplevel, name) => FindWindow(toplevel)?.SetIconName(name);
        _color = _services.Require<Basin.Desktop.ColorManager>();
        _compositor.SurfaceCreated += surface => surface.Destroyed += UpdateEdrDemand;
        DeclareColor();

        _lutDriver = new Basin.Desktop.SurfaceLutDriver(
            _scene, _color,
            surface => _luts.LutFor(_color.DescriptionOf(surface), BlendDescription()));
        _lutDriver.CountChanged += attached => BasinReport.Line($"COLOR luts={attached}");
        _color.SurfaceDescriptionChanged += (_, _) => UpdateEdrDemand();

        _tearing = _services.Require<Basin.Desktop.TearingControlManager>();
        _contentType = _services.Require<Basin.Desktop.ContentTypeManager>();
        _fifo = _services.Require<Basin.Desktop.FifoManager>();
        _frameClock = _services.Require<Basin.Capabilities.IFrameClock>();
        _driver.Frames = _frameClock;
        _xwaylandShell = _services.Require<Basin.XWayland.XwaylandShellGlobal>();
        _xwayland = _services.Require<Basin.XWayland.XWaylandServer>();
        xwayland.WindowManagerReady += wm =>
        {
            _xwm = wm;
            _xSceneDriver.ManagedParent = ManagedXParent;
            _xSceneDriver.OverrideRedirectParent = _ => new SceneTree(_layers.Overlay);
            _xSceneDriver.Adopted += OnXAdopted;
            _xSceneDriver.Removed += OnXRemoved;
            _xSceneDriver.ActivationRequested += ActivateXWindow;
            _xSceneDriver.Attach(wm);
            if (xwayland.Toplevels is { } xToplevels)
            {
                xToplevels.NoBorderRequested += (xwin, noBorder) =>
                    FindXWindow(xwin)?.SetNoBorderOverride(noBorder);
                xToplevels.CaptureExclusionRequested += (xwin, excluded) =>
                    xToplevels.SetExcludedFromCapture(xwin, excluded);
            }

            BasinReport.Line($"XWAYLAND WM {_xwayland.DisplayName}");
        };
        _xwayland.Exited += () => _xwm = null;
        BasinReport.Line($"XWAYLAND {_xwayland.DisplayName}");
        _display.SetGlobalFilter((client, _, interfaceName) =>
            !Basin.Desktop.PrivilegedProtocols.Contains(interfaceName) || IsTrusted(client));
        _textInput = _services.Require<Basin.Desktop.TextInputManager>();
        _lockDriver.TextInput = _textInput;
        _fractionalScale = _services.Require<Basin.Desktop.FractionalScaleManager>();
        _presenceTracker = new SurfacePresenceTracker(_layout, _fractionalScale.AnnounceScale);
        _idle = _services.Require<Basin.Desktop.IdleManager>();
        _idleSource = _services.Require<Basin.Capabilities.IIdleSource>() as Basin.Seat.SeatIdleSource;
        _services.Require<Basin.Desktop.XdgActivationManager>().ActivationRequested += surface =>
        {
            var window = _windows.FirstOrDefault(w => w.Owns(surface));
            if (window is null)
            {
                return;
            }

            if (window.Workspace is { } workspace && ViewOf(workspace) is { } view && view.Active != workspace)
            {
                MarkUrgent(workspace);
            }
            else
            {
                FocusWindow(window);
            }

            RaiseHostWindow(window);
        };
        _cursor = new Basin.Desktop.CursorController(_layout) { Capture = _capture };
        _cursorShapes = _services.Require<Basin.Desktop.CursorShapeManager>();
        _cursorShapes.CursorRequested += _cursor.ShowImage;
        _cursor.Shapes = _cursorShapes;
        _cursor.ColorProfiles = _services.Find<Basin.Capabilities.IColorProfileService>();
        _color.OutputDescriptionChanged += (global, description) => _cursor.Describe(global.Output, description);
        _seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;

        if (drm)
        {
            _pointer = new LayoutPointer(_layout);
            _driver.CreateInitialOutputs();

            if (forcedCard is null)
            {
                foreach (var device in drmDevices)
                {
                    if (device.HasConnectors && device.CardPath != _drm!.DevicePath)
                    {
                        AdoptSecondaryCard(device);
                    }
                }
            }

            _outputsCreated = true;
            SetupTouch();
            WireLibinput(_input!);
            _touchBinder!.PointerFrozen = () => ActiveLock() is not null;
            _touchBinder!.BindLibinput(_input!, start: false);
            WireTablets();
            _input!.Start();
            LoadCursorTheme();
        }
        else if (_backend is null)
        {
            _pointer = new LayoutPointer(_layout);
            _driver.CreateInitialOutputs();
            _outputsCreated = true;
            SetupTouch();
            LoadCursorTheme();
        }
        else
        {
            _backend.RenderDevice = _renderer.Device;

            _backend.ParentGone += () => _runLoop.Stop();
            _backend.PointerAdded += WirePointer;
            _backend.KeyboardAdded += WireKeyboard;
            SetupTouch();
            _touchBinder!.BindParentTouch(_backend);
            _seam = new NestedSeam(
                _backend,
                _services.Find<Basin.Capabilities.ISelectionStore>(),
                _services.Find<Basin.Capabilities.IDragTracker>(),
                _services.Find<Basin.Capabilities.IIdleSource>())
            {
                HostDragMotion = (output, time, x, y) =>
                {
                    var (layoutX, layoutY) = _layout.ToLayout(output, x, y);
                    MoveCursor(layoutX, layoutY, time == 0 ? (uint)Environment.TickCount : time);
                },
            };
            _driver.CreateInitialOutputs();
            _outputsCreated = true;

            _cursor.UseParentCursor();
            LoadCursorTheme();
        }

        _capture.Renderer = _renderer;
        _capture.Background = Background;

        var keymapNames = Basin.Seat.SystemKeymap.Read();
        _seat.Keyboard.SetKeymap(keymapNames);
        BasinReport.Line($"KEYMAP {keymapNames.Layout ?? "xkb default"}{(keymapNames.Model is { } model ? $" {model}" : string.Empty)}");

        _compositor.SurfaceCreated += surface => surface.Committed += () =>
        {
            if (surface.Current.FrameCallbacks.Count > 0)
            {
                foreach (var view in Views)
                {
                    view.Scheduler?.ScheduleRepaint();
                }
            }

            if (_blurEffect is not null && _backgroundEffects.BlurRegionOf(surface) is not null &&
                SceneSurfaceOf(surface) is { } effectScene)
            {
                ApplyBlur(effectScene);
            }
        };

        _shell.NewToplevel += toplevel =>
        {
            var view = Views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? Views.FirstOrDefault();
            if (view is not null)
            {
                var outputBox = _layout.BoxOf(view.Output);
                toplevel.SetBounds(outputBox.Width, outputBox.Height);
                _fractionalScale.AnnounceScale(toplevel.Surface, view.Output.Scale);
            }

            _ = new Window(this, toplevel);
        };
        _shell.NewPopup += WirePopup;
        WireSessions();
        WireStdin();
        _dragIcon = new Basin.Seat.Backends.DragIconFollower(
            _seat, () => _layers.Overlay, () => (_cursorX, _cursorY))
        {
            Touch = _touchDriver?.Router,
        };
        _dragIcon.Created += _ => RefreshSurfaceLuts();
        ApplyEffectSettings(config);
    }

    public long Rendered => _driver.PrimaryRendered;

    private IReadOnlyList<Basin.Host.OutputView> Views => _driver.Views;

    public int Run()
    {
        BasinReport.Line(CompositorLines.Socket(_socket));

        if (_channelEndpoint is { } endpoint)
        {
            System.Net.EndPoint address = endpoint.Contains(':', StringComparison.Ordinal)
                ? System.Net.IPEndPoint.Parse(endpoint)
                : new System.Net.Sockets.UnixDomainSocketEndPoint(endpoint);
            if (address is System.Net.Sockets.UnixDomainSocketEndPoint && File.Exists(endpoint))
            {
                File.Delete(endpoint);
            }

            BasinReport.Line($"CHANNEL {endpoint}");
            _channel = Basin.Transport.Waypipe.WaypipeChannel.Listen(address);
            _channel.Ended += failure => _log.Info($"channel ended{(failure is null ? string.Empty : $": {failure.Message}")}");
            var remote = _display.CreateClient(_channel.Transport);
            var globals = _channel.Globals;
            _display.SetGlobalFilter((client, _, interfaceName) =>
                client != remote || globals.Carries(interfaceName));
            _log.Info($"channel attached; replaying it as one client");
        }

        var hangup = _loop.AddSignal(Signal.Hangup, _ => Reload());
        _runLoop.Frames = _frames;
        _runLoop.Run();
        hangup.Remove();
        return 0;
    }

    public void Dispose()
    {
        _pointerRefresh?.Dispose();
        _stdinCommands?.Stop();
        _stdinCommands = null;
        _channel?.Dispose();
        _channel = null;

        foreach (var window in _windows.ToArray())
        {
            window.SetDecorated(false);
        }

        foreach (var xwindow in _xwindows.ToArray())
        {
            xwindow.DisposeFrame();
        }

        foreach (var chrome in _hostChrome)
        {
            chrome.Dispose();
        }

        _hostChrome.Clear();
        _uiHost?.Dispose();

        _driver.Dispose();

        _cursor.Dispose();

        _xwm?.Dispose();
        _xwayland?.Dispose();
        _protocolTrace?.Dispose();
        _tablets?.Dispose();
        _input?.Dispose();
        _layoutConfiguration?.Dispose();
        _orientationSensor?.Dispose();
        _blitCache?.Dispose();
        foreach (var blitter in _blitters)
        {
            blitter.Dispose();
        }

        foreach (var allocator in _secondaryAllocators)
        {
            allocator.Dispose();
        }

        foreach (var backend in _secondaryBackends)
        {
            backend.Dispose();
        }

        _feedback?.Dispose();
        _feedback = null;
        _effects.Dispose();
        _post.Dispose();
        _shader.Dispose();
        _shadowTexture?.Dispose();
        _dimShader?.Dispose();
        (_blurEffect as IDisposable)?.Dispose();
        foreach (var shader in _cornerShaders.Values)
        {
            shader.Dispose();
        }

        _cornerShaders.Clear();
        _fireShader?.Dispose();
        _allocator?.Dispose();
        _seam?.Dispose();
        _seam = null;
        _seamTextInput?.Dispose();
        _seamTextInput = null;
        _host.Dispose();
        _frameTheme?.Dispose();
        _renderer.Dispose();
    }

    private static readonly bool TraceEnabled = Basin.Diagnostics.BasinDiagnostics.TraceEnabled;

    private readonly BasinLogger _log;

    internal BasinLogger Log => _log;

    private void Trace(string message) =>
        _log.Debug($"T{(Environment.TickCount64 % 1_000_000)} {message}");

    internal interface IGrabTarget
    {
        SceneTree? EffectTree { get; }

        int X { get; }

        int Y { get; }

        (int Width, int Height) GeometrySize { get; }

        void MoveTo(int x, int y);

        void ResizeTo(int x, int y, int width, int height, ResizeEdges edges);

        void SetResizing(bool resizing);
    }

}
