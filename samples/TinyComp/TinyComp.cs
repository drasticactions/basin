using System.Diagnostics;
using Basin;
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
    private readonly DrmFormat _swapFormat = DrmFormat.Xrgb8888;
    private ulong[] _swapModifiers = [];
    private readonly Scene _scene = new();
    private PointerRefresh? _pointerRefresh;
    private SceneLayers _layers = null!;
    private readonly EffectsPolicy _effects;
    private readonly string? _postKind;
    private MagnifierStage? _magnify;
    private InvertStage? _invert;
    private IPixelShader? _invertShader;
    private LayerShell _layerShell = null!;
    private Basin.Desktop.SessionLockManager _sessionLock = null!;
    private Basin.Desktop.SessionLockSceneDriver _lockDriver = null!;
    private readonly XdgDecorationManager _decorations;
    private readonly XdgToplevelDragManager _toplevelDrags;
    private readonly Basin.Desktop.KdeServerDecorationManager _kdeDecorations;
    private readonly Dictionary<Surface, bool> _ssdPreference = [];
    private Basin.Capabilities.IUIHost? _uiHost;
    private readonly FrameStyle _frameStyle;
    private FrameTheme? _frameTheme;

    internal IFrameRenderer? CreateFrameRenderer() => _frameStyle switch
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
    private readonly OutputState _frameState = new();
    private readonly List<OutputView> _views = [];
    private readonly List<HostChrome> _hostChrome = [];
    private bool _probed;
    private readonly List<Basin.Backend.Drm.DrmBackend> _secondaryBackends = [];
    private readonly List<IAllocator> _secondaryAllocators = [];
    private readonly List<Basin.Render.Vulkan.VulkanDeviceBlitter> _blitters = [];

    private IBackdropEffect? _blurEffect;
    private IPixelShader? _cornerShader;
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
    private readonly bool _offload;
    private readonly bool _hdr;
    private readonly Basin.Capabilities.OutputColorProfileSource _colorSource;
    private readonly string? _iccProfile;
    private readonly bool _damageTint;
    private readonly double[] _scales;
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

    public TinyComp(int outputCount, string rendererName = "vulkan", bool drm = false, bool fullRepaint = false, bool damageTint = false, double[]? scales = null, bool offload = true, double? nightLight = null, bool hdr = false, Basin.Capabilities.OutputColorProfileSource colorSource = Basin.Capabilities.OutputColorProfileSource.Edid, FrameStyle frameStyle = FrameStyle.Beos, bool noTransactions = false, int socketFd = -1, BasinLogger log = default, bool wobbly = false, string? openAnimation = null, string? post = null, string? closeAnimation = null, bool switcher = false, int cornerRadius = 0, long frameLimit = 0, bool managedTransport = false, string? channelEndpoint = null, string? iccProfile = null)
    {
        _log = log;
        _iccProfile = iccProfile;
        _frames = frameLimit;
        _effects = new EffectsPolicy(wobbly, openAnimation, closeAnimation, switcher);
        _postKind = post;
        _useTransactions = !noTransactions;
        _frameStyle = frameStyle;
        _fullRepaint = fullRepaint;
        _offload = offload;
        _nightLightKelvin = nightLight;
        _hdr = hdr;
        _colorSource = colorSource;
        _damageTint = damageTint;
        _scales = scales ?? [];
        _host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(drm ? "drm" : "nested") with
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
                _log.Info($"client connected: pid={credentials.Pid} uid={credentials.Uid}");
            }
            else
            {
                _log.Info($"client connected: no local process behind it");
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

            _allocator = stack.DeviceAllocator ?? new Basin.Backend.Drm.DumbAllocator(_drm);
        }
        else
        {
            _backend = _host.Parent!;

            var stack = CreateStack(ref rendererName, Basin.Renderers.RendererCatalog.FindRenderNode());
            _renderer = stack.Renderer;

            if (stack.DeviceAllocator is { } gbm)
            {
                _swapModifiers = SwapchainFormats.CommonModifiers(gbm, _backend.ParentDmabufFormats, _swapFormat);
                if (_swapModifiers.Length > 0)
                {
                    _allocator = gbm;
                    _log.Info($"{rendererName} zero-copy: {_swapModifiers.Length} modifiers for {_swapFormat}");
                }
                else
                {
                    gbm.Dispose();
                    _allocator = new ShmAllocator();
                    _log.Info($"{rendererName} rendering with shm-copy presentation (no common dmabuf format with parent)");
                }
            }
            else
            {
                _allocator = new ShmAllocator();
                _log.Info($"{rendererName} rendering with shm-copy presentation");
            }
        }

        _popupPlacer = new Basin.Desktop.PopupPlacer(_layout);
        _luts = new Basin.Color.ColorLutCache(_renderer);
        _effects.SlideEnabled = rendererName != "pixman";

        var servicePack = new Basin.Desktop.DesktopServicePack(_scene, _layout, _renderer, _drm);
        var capturePack = servicePack.Capture;
        _capture = capturePack.Capture;
        _dmabufCapture = capturePack.DmabufCapture;
        _captureIndex = capturePack.Index;
        _stack = capturePack.Stack;
        _gamma = servicePack.Drm.Gamma;
        _cursorTheme = servicePack.CursorTheme;
        _orientationSensor = new Basin.Desktop.OrientationSensor(_loop);
        _colorPack = new Basin.Color.ColorCapabilityPack(_layout);
        _layoutConfiguration = _colorPack.Layout;
        _layoutConfiguration.Orientation = _orientationSensor;
        _colorConfiguration = _colorPack.Configuration;
        _colorConfiguration.EdrChanged += output =>
        {
            if (_views.FirstOrDefault(v => v.Output == output) is { } edrView)
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

        if (closeAnimation == "fire-gpu")
        {
            _fireShader = _renderer.CompilePixelShader(Basin.Effects.FireShader.Source, Basin.Effects.FireShader.Uniforms);
            if (_fireShader is null)
            {
                _log.Warn($"{rendererName} compiles no pixel shader dialect; fire-gpu falls back to the particle mesh");
            }

            _effects.FireShaderHandle = _fireShader;
        }

        if (cornerRadius > 0)
        {
            _cornerShader = _renderer.CompilePixelShader(CornerShader.Source, CornerShader.Uniforms);
            if (_cornerShader is null)
            {
                _log.Warn($"{rendererName} compiles no pixel shader dialect; --corner-radius is ignored");
            }
            else
            {
                _cornerRadius = cornerRadius;
                var cornerScale = _scales.Length > 0 ? _scales[0] : 1.0;
                _cornerShader.SetUniforms([(float)(cornerRadius * cornerScale)]);
            }
        }

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
        _presentation = _services.Require<PresentationTimeGlobal>();
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
            foreach (var output in _drm!.Outputs)
            {
                AddDrmOutput(output);
            }

            _drm.OutputAdded += output =>
            {
                BasinReport.Line($"OUTPUT + {output.Name}");
                AddDrmOutput(output);
                Relayout();
            };
            _drm.OutputRemoved += RemoveDrmOutput;

            if (forcedCard is null)
            {
                foreach (var device in drmDevices)
                {
                    if (device.HasConnectors && device.CardPath != _drm.DevicePath)
                    {
                        AdoptSecondaryCard(device);
                    }
                }
            }

            SetupTouch();
            WireLibinput(_input!);
            _touchBinder!.PointerFrozen = () => ActiveLock() is not null;
            _touchBinder!.BindLibinput(_input!, start: false);
            WireTablets();
            _input!.Start();
            LoadCursorTheme();
        }
        else
        {
            _backend!.RenderDevice = _renderer.Device;

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
            for (var i = 0; i < outputCount; i++)
            {
                AddOutput();
            }

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
                foreach (var view in _views)
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
            var view = _views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? _views.FirstOrDefault();
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
    }

    public long Rendered => _views.Count > 0 ? _views[0].Rendered : 0;

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

        _runLoop.Frames = _frames;
        _runLoop.Run();
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

        foreach (var view in _views)
        {
            view.Scheduler?.Dispose();
            view.SceneOutput?.Dispose();
            view.Swapchain?.Dispose();
            view.Target?.Destroy();
        }

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

        (_blurEffect as IDisposable)?.Dispose();
        _cornerShader?.Dispose();
        _fireShader?.Dispose();
        _invertShader?.Dispose();
        _allocator?.Dispose();
        _frameState.Dispose();
        _seam?.Dispose();
        _seam = null;
        _seamTextInput?.Dispose();
        _seamTextInput = null;
        _host.Dispose();
        _frameTheme?.Dispose();
        _renderer.Dispose();
    }

    private Basin.Render.Vulkan.VulkanDeviceBlitter? BlitterFor(ulong deviceId)
    {
        foreach (var blitter in _blitters)
        {
            if (DrmDevices.TryDeviceId(blitter.DevicePath, out var id) && id == deviceId)
            {
                return blitter;
            }
        }

        return null;
    }

    private RenderStack CreateStack(ref string rendererName, string? renderNodePath) =>
        Basin.Renderers.RendererCatalog.CreateWithFallback(ref rendererName, renderNodePath, ReportFallback);

    private Basin.Capabilities.ImageDescription DescriptionOf(IOutput output) =>
        _colorConfiguration is { } configuration
            ? configuration.DescriptionOf(output)
            : Basin.Capabilities.ImageDescription.Srgb;

    private void DeclareColor()
    {
        if (_color is { } color)
        {
            Basin.Desktop.SurfaceLutDriver.Declare(color, _views.Select(v => DescriptionOf(v.Output)));
        }
    }

    private void ReportFallback(Basin.Renderers.RendererFallback fallback) =>
        _log.Warn($"{(fallback.Describe())}");

    private double ScaleFor(int index) =>
        _scales.Length == 0 ? 1 : _scales[Math.Min(index, _scales.Length - 1)];

    private void AddOutput()
    {
        var output = _backend!.CreateOutput();
        var view = new OutputView(output, new OutputGlobal(_display, output));
        _views.Add(view);
        _presenceTracker.AddOutput(view.Output, view.Global);
        InitWorkspaces(view);
        var scale = ScaleFor(_views.Count - 1);
        if (scale != 1)
        {
            using var state = new OutputState();
            output.Commit(state.SetScale(scale));
        }

        if (_scales.Length == 0)
        {
            output.HostScaleChanged += () =>
            {
                if (output.HostScale != output.Scale)
                {
                    SetOutputScale(view, output.HostScale);
                }
            };
        }

        _layout.Add(output, 0, 0);
        Relayout();
        output.CloseRequested += () => _runLoop.Stop();
        output.Committed += _ => OnOutputChanged(view);
        output.Committed += _ => { if (!_probed && output.CurrentMode.Width > 0) { _probed = true; BasinReport.Line($"PROBE decorated={output.Decorated} hostFrame={(output.HostFrame is null ? "none" : "yes")} insets={(output.HostFrame is null ? "-" : output.HostFrame.Insets.ToString())} mode={output.CurrentMode.Width}x{output.CurrentMode.Height}"); } };
        WireRepaint(view);
        _cursor.AddOutput(view.Output, view.SceneOutput);

        output.HostFrameAvailable += frame =>
        {
            if (CreateFrameRenderer() is not { } renderer)
            {
                return;
            }

            var chrome = new HostChrome(this, frame, renderer, $"basin — {output.Name}", () => _runLoop.Stop());
            _hostChrome.Add(chrome);
            output.Committed += chrome.OnOutputChanged;
        };
    }

    private void WireRepaint(OutputView view)
    {
        if (_fullRepaint)
        {
            view.Output.Frame += () => RenderOutput(view);
            return;
        }

        view.Scheduler = new OutputScheduler(_loop, view.Output);
        view.Scheduler.Repaint += () => Repaint(view);
        if (view.IsSecondary)
        {
            _scene.Damaged += (_, box) =>
            {
                var outputBox = view.ReplicaSource is { } replicaSource
                    ? _layout.BoxOf(replicaSource.Output)
                    : _layout.BoxOf(view.Output);
                if (!outputBox.IsEmpty && box.X < outputBox.X + outputBox.Width && box.X + box.Width > outputBox.X &&
                    box.Y < outputBox.Y + outputBox.Height && box.Y + box.Height > outputBox.Y)
                {
                    view.Scheduler.ScheduleRepaint();
                }
            };
            return;
        }

        view.SceneOutput = new SceneOutput(_scene, view.Output);
        if (_effects.Any)
        {
            view.SceneOutput.BeforeRepaint += tick =>
            {
                if (_effects.Step(tick))
                {
                    foreach (var animated in _views)
                    {
                        animated.Scheduler?.ScheduleRepaint();
                    }
                }
            };
        }

        if (_postKind == "invert")
        {
            _invertShader ??= _renderer.CompilePixelShader(InvertShader.Source, InvertShader.Uniforms);
            _invert ??= new InvertStage(_invertShader);
            view.SceneOutput.AddPostStage(_invert);
        }
        else if (_postKind == "magnify")
        {
            _magnify ??= new MagnifierStage();
            _magnify.TargetZoom = 2.0;
            view.SceneOutput.AddPostStage(_magnify);
        }

        _dmabufCapture.Track(view.Output, view.SceneOutput);

        view.SceneOutput.DamagePending += view.Scheduler.ScheduleRepaint;
        view.SceneOutput.ScanoutCandidateChanged += surface => OnScanoutCandidate(view, surface);
        view.SceneOutput.OffloadCandidatesChanged += candidates => OnOffloadCandidates(view, candidates);
        if (view.Output is IPresentingOutput presenting)
        {
            presenting.PresentedOnScreen += (timeNs, refreshNs, sequence) =>
            {
                view.PresentDiscarded = false;
                view.LastPresent = (timeNs, refreshNs, sequence);
                view.Scheduler!.NotifyPresented((long)timeNs);
            };
            presenting.PresentationDiscarded += () =>
            {
                view.LastPresent = null;
                view.PresentDiscarded = true;
            };
        }

        view.Output.Frame += () =>
        {
            if (!view.FrameDonesPending)
            {
                return;
            }

            view.FrameDonesPending = false;
            if (view.PresentDiscarded)
            {
                view.PresentDiscarded = false;
                _presentation.DiscardAll();
                _frameClock.EndFrame(view.Output, 0);
            }
            else if (view.LastPresent is { } present)
            {
                view.LastPresent = null;
                _presentation.PresentAll(view.Output, present.TimeNs, present.RefreshNs, present.Sequence,
                    PresentedFlags.Vsync | PresentedFlags.HwClock | PresentedFlags.HwCompletion);
                _frameClock.EndFrame(view.Output, (long)present.TimeNs);
            }
            else
            {
                _presentation.PresentAllNow(view.Output);
                _frameClock.EndFrame(view.Output, MonotonicClock.Nanos);
            }
        };
        if (TraceEnabled)
        {
            view.SceneOutput.DamagePending += () => Trace("damage");
            view.Output.Frame += () => Trace($"frame {view.Output.Name}");
        }
    }

    private PresentationTimeGlobal _presentation = null!;

    private static readonly bool TraceEnabled = Basin.Diagnostics.BasinDiagnostics.TraceEnabled;

    private readonly BasinLogger _log;

    internal BasinLogger Log => _log;

    private void Trace(string message) =>
        _log.Debug($"T{(Environment.TickCount64 % 1_000_000)} {message}");

    private void Repaint(OutputView view)
    {
        _frameClock.BeginFrame(view.Output, view.Scheduler!.PredictedVblankNanos);
        if (view.IsSecondary)
        {
            SecondaryRepaint(view);
            return;
        }

        if (view.Swapchain is null || view.SceneOutput is null)
        {
            return;
        }

        if (_frames > 0)
        {
            view.SceneOutput.Ring.AddWhole();
        }

        var box = new Box(0, 0, view.Width, view.Height);
        if (view.ReplicaSource is { } replicaSource && _layout.Contains(replicaSource.Output))
        {
            view.SceneOutput.ReplicationSource = _layout.BoxOf(replicaSource.Output);
        }
        else
        {
            view.SceneOutput.ReplicationSource = null;
            if (_layout.Contains(view.Output))
            {
                box = _layout.BoxOf(view.Output);
                view.SceneOutput.Position = new Point(box.X, box.Y);
            }
        }

        var renderStart = TraceEnabled ? Stopwatch.GetTimestamp() : 0;
        var options = new SceneCommitOptions
        {
            Background = Background,
            DebugDamageTint = _damageTint,
            AllowPlaneOffload = _offload,
            TargetPresentNanos = Math.Max(
                view.Scheduler!.PredictedVblankNanos,
                (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency))),
        };
        if (_magnify is { } magnify)
        {
            magnify.CenterX = _cursorX;
            magnify.CenterY = _cursorY;
            if (magnify.Step(new FrameTick(options.TargetPresentNanos, 0)))
            {
                view.Scheduler?.ScheduleRepaint();
            }
        }

        _frameState.Clear();
        var autoVrr = false;
        if (_scanoutFeedbackSurface is { } scanoutSurface)
        {
            if (_tearing.PrefersTearing(scanoutSurface))
            {
                _frameState.SetTearing(true);
            }

            var contentType = _contentType.TypeOf(scanoutSurface);
            if (contentType is Basin.Desktop.ContentTypeManager.ContentType.Game
                    or Basin.Desktop.ContentTypeManager.ContentType.Video &&
                VrrPolicyOf(view.Output) == Basin.Capabilities.OutputVrrPolicy.Automatic)
            {
                _frameState.SetAdaptiveSync(true);
                autoVrr = true;
            }
        }

        if (!autoVrr && view.AutoVrrActive)
        {
            _frameState.SetAdaptiveSync(false);
        }

        view.AutoVrrActive = autoVrr;

        var committed = view.SceneOutput.Commit(_renderer, view.Swapchain, _frameState, options);
        var refused = !committed && view.SceneOutput.NeedsRepaint;
        if (committed)
        {
            view.Scheduler!.NotifyCommitted();
            view.LastPresentedBuffer = _frameState.Buffer;
            view.Rendered++;
        }
        else if (refused)
        {
            view.Scheduler!.ScheduleRepaint();
        }

        if (TraceEnabled)
        {
            var renderMs = (Stopwatch.GetTimestamp() - renderStart) * 1000.0 / Stopwatch.Frequency;
            Trace($"repaint committed={committed} refused={refused} renderMs={renderMs:F2}");
        }

        MaybeScreenshotDamage(view, box);
        if (refused)
        {
            return;
        }

        _capture.NotifyDamaged(view.Output, new Box(0, 0, view.Width, view.Height));
        UpdateSurfacePresence();
        if (committed)
        {
            view.FrameDonesPending = true;
        }

        _scene.SendFrameDone((uint)Environment.TickCount);
        if (_frames > 0)
        {
            view.Scheduler!.ScheduleRepaint();
        }
    }

    private void DumpTree(SceneNode node, int depth)
    {
        var info = node switch
        {
            SceneBuffer b => $"buffer {(b.Buffer is null ? "empty" : $"{b.Buffer.Width}x{b.Buffer.Height}")} opaque={b.IsOpaque}",
            SceneRect r => $"rect {r.Width}x{r.Height}",
            SceneTree => "tree",
            _ => node.GetType().Name,
        };
        _log.Debug($"DBG {(new string(' ', depth * 2))}{info} at ({node.X},{node.Y}) enabled={node.Enabled}");
        if (node is SceneTree tree)
        {
            foreach (var child in tree.Children)
            {
                DumpTree(child, depth + 1);
            }
        }
    }

    private readonly List<SurfaceBox> _presence = [];
    private SurfacePresenceTracker _presenceTracker = null!;

    private void UpdateSurfacePresence()
    {
        _scene.CollectSurfaces(_presence);
        _presenceTracker.Update(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_presence));
    }

    private readonly HashSet<Surface> _offloadFeedback = [];
    private readonly List<Surface> _offloadFeedbackGone = [];

    private void OnOffloadCandidates(OutputView view, IReadOnlyList<Basin.Scene.SceneBuffer> candidates)
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
            if (surface != _scanoutFeedbackSurface)
            {
                _dmabufGlobal.SetScanoutTargets(surface, null);
            }
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].InputSurface is { } surface &&
                surface != _scanoutFeedbackSurface &&
                _offloadFeedback.Add(surface))
            {
                _dmabufGlobal.SetScanoutTargets(surface, drmOutput.OverlayScanoutFormats);
            }
        }
    }

    private readonly Basin.Color.ColorLutCache _luts;
    private Basin.Desktop.SurfaceLutDriver _lutDriver = null!;

    private Basin.Capabilities.ImageDescription BlendDescription() =>
        _views.Count == 0 || _views[0].KmsColorRouted
            ? Basin.Capabilities.ImageDescription.Srgb
            : _views[0].ColorDescription;

    private void RefreshSurfaceLuts()
    {
        _lutDriver.Refresh();
        UpdateEdrDemand();
    }

    private void UpdateEdrDemand()
    {
        if (_colorConfiguration is not { } colorConfiguration)
        {
            return;
        }

        var demand = 1.0;
        foreach (var surface in _compositor.Surfaces)
        {
            if (_color.DescriptionOf(surface) is not { } description)
            {
                continue;
            }

            var characteristics = Basin.Color.TransferCharacteristics.From(description);
            if (characteristics.MaxLuminance > characteristics.ReferenceLuminance)
            {
                demand = Math.Max(demand, characteristics.MaxLuminance / characteristics.ReferenceLuminance);
            }
        }

        foreach (var view in _views)
        {
            colorConfiguration.SetEdrDemand(view.Output, demand);
        }
    }

    private double? _nightLightKelvin;

    private void ApplyNightLight(double? kelvin)
    {
        _nightLightKelvin = kelvin;
        foreach (var view in _views)
        {
            if (_gamma.RampSize(view.Output) > 0)
            {
                RefreshGammaBaseline(view);
                _gamma.ApplyBaseline(view.Output);
            }
        }
    }

    private void RefreshGammaBaseline(OutputView view)
    {
        if (_gamma.RampSize(view.Output) is var size && size > 0)
        {
            _gamma.Baseline = _nightLightKelvin is { } k
                ? NightLightRamps(view, (int)size, k)
                : view.KmsColorRouted && _colorConfiguration is { } configuration
                    ? configuration.RoutedEncodeRamps(view.Output)
                    : null;
        }
    }

    private OutputGammaRamps NightLightRamps(OutputView view, int size, double kelvin)
    {
        if (view.KmsColorRouted && _colorConfiguration is { } configuration &&
            configuration.RoutedEncodeRamps(view.Output, Basin.Color.NightLight.Multipliers(kelvin)) is { } routed)
        {
            return routed;
        }

        var ramps = new OutputGammaRamps(new ushort[size], new ushort[size], new ushort[size]);
        Basin.Color.NightLight.FillGammaRamps(kelvin, ramps.Red, ramps.Green, ramps.Blue);
        return ramps;
    }

    private void OnScanoutCandidate(OutputView view, Surface? surface)
    {
        if (_dmabufGlobal is null)
        {
            return;
        }

        if (_scanoutFeedbackSurface is { } previous && previous != surface)
        {
            _dmabufGlobal.SetScanoutTargets(previous, null);
        }

        _scanoutFeedbackSurface = surface;
        if (surface is not null && view.Output is Basin.Backend.Drm.DrmOutput drmOutput)
        {
            _dmabufGlobal.SetScanoutTargets(surface, drmOutput.ScanoutFormats);
        }
    }

    private bool RenderForCapture(IOutput output, IBuffer target)
    {
        var box = _layout.BoxOf(output);
        _scene.Root.SetPosition(-box.X, -box.Y);
        var ok = _scene.Render(_renderer, target, SceneOptions(output));
        _scene.Root.SetPosition(0, 0);
        return ok;
    }

    private Box ToplevelBox(Basin.Shell.Xdg.XdgToplevelWindow toplevel)
    {
        var window = _windows.FirstOrDefault(w => w.Toplevel == toplevel);
        var surface = toplevel.Surface.Current;
        return window is null
            ? default
            : new Box(window.X, window.Y, surface.Width, surface.Height);
    }

    private double ScaleOfBox(Box box)
    {
        var scale = 1.0;
        foreach (var view in _views)
        {
            var outputBox = _layout.BoxOf(view.Output);
            if (box.X < outputBox.Right && box.Right > outputBox.X &&
                box.Y < outputBox.Bottom && box.Bottom > outputBox.Y)
            {
                scale = Math.Max(scale, view.Output.Scale);
            }
        }

        return scale;
    }

    private bool RenderToplevelCapture(Basin.Shell.Xdg.XdgToplevelWindow toplevel, IBuffer target)
    {
        var box = ToplevelBox(toplevel);
        if (box.Width <= 0 || box.Height <= 0)
        {
            return false;
        }

        _scene.Root.SetPosition(-box.X, -box.Y);
        var ok = _scene.Render(_renderer, target, SceneOptions(ScaleOfBox(box)));
        _scene.Root.SetPosition(0, 0);
        return ok;
    }

    private void MaybeScreenshotDamage(OutputView view, Box box)
    {
        if (_shotPath is null || view != _views[_shotView])
        {
            return;
        }

        _scene.Root.SetPosition(-box.X, -box.Y);
        MaybeScreenshot(view);
        _scene.Root.SetPosition(0, 0);
    }

    private void RemoveDrmOutput(Basin.Backend.Drm.DrmOutput output)
    {
        BasinReport.Line($"OUTPUT - {output.Name}");
        var view = _views.FirstOrDefault(v => v.Output == output);
        if (view is not null)
        {
            if (view == _swipeView)
            {
                AbortWorkspaceSwipe();
            }

            _views.Remove(view);
            _presenceTracker.RemoveOutput(view.Output);
            DropWorkspacesOf(view);
            _dmabufCapture.Forget(view.Output);
            view.Swapchain?.Dispose();
            view.Global.Dispose();
        }

        Relayout();
    }

    private void AdoptSecondaryCard(DrmDeviceInfo device)
    {
        try
        {
            var backend = new Basin.Backend.Drm.DrmBackend(_loop, _session!, device.CardPath);
            backend.Start();
            _secondaryBackends.Add(backend);
            var allocator = new Basin.Backend.Drm.DumbAllocator(backend);
            _secondaryAllocators.Add(allocator);
            BasinReport.Line($"CARD + {device.CardPath} ({device.Driver})");
            foreach (var output in backend.Outputs)
            {
                AddDrmOutput(output, allocator, secondary: true);
            }

            backend.OutputAdded += output =>
            {
                BasinReport.Line($"OUTPUT + {output.Name}");
                AddDrmOutput(output, allocator, secondary: true);
                Relayout();
            };
            backend.OutputRemoved += RemoveDrmOutput;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            BasinReport.Line($"CARD {device.CardPath} not adopted: {e.Message}");
        }
    }

    private void AddDrmOutput(Basin.Backend.Drm.DrmOutput output, IAllocator? allocator = null, bool secondary = false)
    {
        var view = new OutputView(output, new OutputGlobal(_display, output))
        {
            IsSecondary = secondary,
            Allocator = allocator ?? _allocator,
        };
        _views.Add(view);
        _presenceTracker.AddOutput(view.Output, view.Global);
        InitWorkspaces(view);
        _layout.Add(output, 0, 0);
        Relayout();
        output.Committed += _ => OnOutputChanged(view);
        WireRepaint(view);
        _cursor.AddOutput(view.Output, view.SceneOutput);

        view.SwapModifiers = view.Allocator!.Formats.Intersect(output.ScanoutFormats).ModifiersOf(_swapFormat).ToArray();
        if (view.Allocator is not Basin.Backend.Drm.DumbAllocator &&
            !view.Allocator!.CanScanOut(output, view.SwapModifiers, DrmFormat.Xrgb8888))
        {
            _log.Warn(
                $"{output.Name}: the renderer shares no scanout format with this plane; " +
                $"presenting through CPU-mapped buffers, which reads the whole framebuffer back every frame");
            view.Allocator = new Basin.Backend.Drm.DumbAllocator(_drm!);
            view.SwapModifiers = [];
        }

        if (!secondary)
        {
            _swapModifiers = view.SwapModifiers;
        }

        BasinReport.Line($"OUTPUT {output.Name} {output.Description} {output.PreferredMode.Width}x{output.PreferredMode.Height} scanout-modifiers={view.SwapModifiers.Length}{(secondary ? " secondary" : "")}");

        var driveHdr = _hdr && output.Edid.SupportsPq;
        if (driveHdr && _colorConfiguration is { } colorConfiguration &&
            (colorConfiguration.Supported(output) &
             Basin.Capabilities.OutputConfigurationFeatures.HighDynamicRange) != 0)
        {
            colorConfiguration.Seed(
                output,
                new Basin.Color.OutputColorState { HighDynamicRange = true, Source = _colorSource });
        }
        else if (_colorSource != Basin.Capabilities.OutputColorProfileSource.Edid &&
            _colorConfiguration is { } sdrConfiguration)
        {
            sdrConfiguration.Seed(
                output, new Basin.Color.OutputColorState { Source = _colorSource, IccProfilePath = _iccProfile });
        }

        view.ColorDescription = DescriptionOf(output);
        view.KmsColorRouted = _colorConfiguration is { } routing && routing.RouteKmsPipeline(output);
        DeclareColor();
        _color.SetOutputDescription(view.Global, view.ColorDescription);
        RefreshSurfaceLuts();
        if (driveHdr)
        {
            BasinReport.Line($"HDR {output.Name} PQ peak={output.Edid.MaxLuminance:F0}cd/m2 bt2020={output.Edid.SupportsBt2020}");
        }

        _frameState.Clear();
        _frameState.SetEnabled(true).SetMode(output.PreferredMode).SetScale(ScaleFor(_views.Count - 1));
        if (driveHdr)
        {
            _frameState.SetHdr(Basin.Color.OutputDescriptions.HdrMetadataFor(view.ColorDescription, output.Edid.Chromaticities));
        }

        output.Commit(_frameState);
        RefreshGammaBaseline(view);
        if (_nightLightKelvin is not null && output.GammaLutSize > 0)
        {
            _gamma.ApplyBaseline(view.Output);
        }

        if (_fullRepaint)
        {
            RenderOutput(view);
        }
        else
        {
            view.Scheduler!.ScheduleRepaint();
        }
    }

    private void WireTablets()
    {
        var tablets = _services.Require<Basin.Desktop.TabletManager>();
        tablets.ToolProximityIn += (tool, _, axes) => AimTool(tool, axes);
        tablets.ToolMoved += AimTool;
    }

    private void AimTool(Basin.Desktop.TabletManager.TabletTool tool, Basin.Capabilities.TabletToolAxes axes)
    {
        if (_views.Count == 0)
        {
            return;
        }

        _idle.NotifyActivity();
        Basin.Desktop.TabletAiming.AimAt(tool, _scene, _layout, _cursor.CursorOutput ?? _views[0].Output, axes);
    }

    private void WireLibinput(Basin.Backend.Libinput.LibinputBackend input)
    {
        input.SwitchToggled += (_, switchEvent) =>
        {
            if (switchEvent.Switch == Libinput.LibinputSwitchType.TabletMode &&
                _layoutConfiguration is { } layoutConfiguration)
            {
                layoutConfiguration.TabletMode = switchEvent.State == Libinput.LibinputSwitchState.On;
            }
        };

        input.DeviceAdded += device =>
        {
            BasinReport.Line($"INPUT + {device.Name}");
            ConfigureTouchpad(device);
        };
        input.DeviceRemoved += device => BasinReport.Line($"INPUT - {device.Name}");
        _touchBinder!.Key += (time, key, pressed) =>
        {
            _idle.NotifyActivity();
            HandleKey(time, key, pressed);
        };
        _touchBinder!.Button += OnButton;
        _touchBinder!.Motion += (time, dx, dy, dxu, dyu) =>
        {
            _idle.NotifyActivity();
            if (dxu is { } unacceleratedDx && dyu is { } unacceleratedDy)
            {
                _relativePointer.NotifyMotion((ulong)time * 1000, dx, dy, unacceleratedDx, unacceleratedDy);
            }

            if (ActiveLock() is not null)
            {
                return;
            }

            OnPointerPlaced(time);
        };
        _touchBinder!.Axis += (time, axis) => _seat.Pointer.NotifyAxis(time, axis);
        input.Gesture += (_, type, gesture) =>
        {
            _idle.NotifyActivity();
            var time = (uint)(gesture.TimestampMicroseconds / 1000);
            switch (type)
            {
                case Libinput.LibinputEventType.GestureSwipeBegin:
                    if (!BeginWorkspaceSwipe((uint)gesture.FingerCount, time))
                    {
                        _gestures.NotifySwipeBegin(time, (uint)gesture.FingerCount);
                    }

                    break;
                case Libinput.LibinputEventType.GestureSwipeUpdate:
                    if (!UpdateWorkspaceSwipe(gesture.Dx, gesture.Dy, time))
                    {
                        _gestures.NotifySwipeUpdate(time, gesture.Dx, gesture.Dy);
                    }

                    break;
                case Libinput.LibinputEventType.GestureSwipeEnd:
                    if (!EndWorkspaceSwipe(gesture.Cancelled, time))
                    {
                        _gestures.NotifySwipeEnd(time, gesture.Cancelled);
                    }

                    break;
                case Libinput.LibinputEventType.GesturePinchBegin:
                    _gestures.NotifyPinchBegin(time, (uint)gesture.FingerCount);
                    break;
                case Libinput.LibinputEventType.GesturePinchUpdate:
                    _gestures.NotifyPinchUpdate(time, gesture.Dx, gesture.Dy, gesture.Scale, gesture.AngleDelta);
                    break;
                case Libinput.LibinputEventType.GesturePinchEnd:
                    _gestures.NotifyPinchEnd(time, gesture.Cancelled);
                    break;
                case Libinput.LibinputEventType.GestureHoldBegin:
                    _gestures.NotifyHoldBegin(time, (uint)gesture.FingerCount);
                    break;
                case Libinput.LibinputEventType.GestureHoldEnd:
                    _gestures.NotifyHoldEnd(time, gesture.Cancelled);
                    break;
            }
        };
    }

    private static void ConfigureTouchpad(Basin.Backend.Libinput.InputDevice device)
    {
        var config = device.Config;
        if (config.Tap.FingerCount > 0)
        {
            config.Tap.Enabled = true;
        }

        if (config.Click.Methods.HasFlag(Libinput.LibinputClickMethod.Clickfinger))
        {
            config.Click.Method = Libinput.LibinputClickMethod.Clickfinger;
        }
    }

    private const int TouchGripSlop = 12;
    private const int TouchRingMargin = 32;
    private const int TouchCornerZone = 40;
    private const int TouchSplitGrabZone = 16;

    private readonly Basin.Seat.CentroidSwipeGesture _touchSwipeGesture = new()
    {
        Fingers = TouchSwipeFingers,
        Slop = TouchSwipeSlop,
    };

    private Basin.Seat.Backends.SeatBinder? _touchBinder;
    private Basin.Seat.Backends.SeatTouchDriver? _touchDriver;
    private Basin.Seat.TouchMoveResize? _touchMoveResize;
    private readonly Basin.Shell.Xdg.GrabOrigin _grabOrigin;
    private (Frame Frame, IGrabTarget Owner)? _touchFramePress;

    private void SetupTouch()
    {
        _touchBinder = new Basin.Seat.Backends.SeatBinder(
            _seat, _layout, _pointer ?? new LayoutPointer(_layout), _cursor)
        {
            Drm = _drm,
            Theme = _cursorTheme,
        };
        _touchDriver = new Basin.Seat.Backends.SeatTouchDriver(_touchBinder, _seat);
        _touchMoveResize = _touchDriver.MoveResize;
        _grabOrigin.Touch = _touchMoveResize;
        _touchMoveResize.Handler = this;
        _touchSwipeGesture.Handler = this;
        _touchDriver.Router.HitTester = new Basin.Seat.Backends.SceneTouchHitTester(_scene);
        _touchDriver.Router.Chrome = this;
        _touchDriver.Router.Gestures = _touchSwipeGesture;
        _touchDriver.Router.Activity = this;
        _touchDriver.AttachPointer(this);
        _touchDriver.Routed += (_, _, surface) =>
        {
            if (surface is not null)
            {
                FocusSurfaceOwner(surface);
            }
        };
    }

    void Basin.Seat.ITouchActivitySink.OnTouchActivity() => _idle.NotifyActivity();

    void Basin.Seat.ITouchPointerTarget.Warp(uint timeMs, double x, double y) => MoveCursor(x, y, timeMs);

    void Basin.Seat.ITouchPointerTarget.Button(uint timeMs, uint button, bool pressed) =>
        OnButton(timeMs, button, pressed);

    void Basin.Seat.ITouchDragHandler.DragTo(double x, double y) => DragTo(x, y);

    void Basin.Seat.ITouchDragHandler.DragEnd(bool cancelled) => EndTouchDrag();

    bool Basin.Seat.ITouchChrome.TryPress(int id, uint timeMs, double x, double y)
    {
        var topFrame = _scene.NodeAt(x, y) is { Node: { } topNode } ? FindFrame(topNode) : null;
        if (topFrame is null && _scene.SurfaceAt(x, y) is not null)
        {
            return false;
        }

        if (_mode != DragMode.None || _touchFramePress is not null)
        {
            return true;
        }

        if (ViewAt(x, y)?.Active is { Tiled.Count: 2 } tiled &&
            Math.Abs(x - SplitX(tiled)) <= TouchSplitGrabZone &&
            y >= tiled.TileArea.Y && y < tiled.TileArea.Bottom)
        {
            BeginSplitDrag(tiled);
            _ = _touchMoveResize!.TryBeginContact(id, out _, out _);
            return true;
        }

        if (_scene.NodeAt(x, y) is { Node: { } frameNode } && FindFrame(frameNode) is { } frameHit)
        {
            FocusFrameOwner(frameHit.Owner);
            PrepareMenu(frameHit);
            _touchFramePress = frameHit;
            _grabOrigin.FrameTouchSlot = id;
            frameHit.Frame.TouchDown(x - frameHit.Owner.X, y - frameHit.Owner.Y, id, timeMs);
            _grabOrigin.FrameTouchSlot = null;
            if (frameHit.Frame.IsMenuOpen)
            {
                _openMenu = frameHit.Frame;
            }

            return true;
        }

        if (TryRingResize(x, y, TouchRingMargin, TouchCornerZone, out var ringEdges, out var ringWindow, out var ringXWindow))
        {
            _grabOrigin.FrameTouchSlot = id;
            if (ringWindow is not null)
            {
                FocusWindow(ringWindow);
                BeginResize(ringWindow, ringEdges);
            }
            else if (ringXWindow is not null)
            {
                FocusXWindow(ringXWindow);
                BeginResize(ringXWindow, ringEdges);
            }

            _grabOrigin.FrameTouchSlot = null;
            return true;
        }

        return false;
    }

    void Basin.Seat.ITouchChrome.Motion(int id, uint timeMs, double x, double y)
    {
    }

    void Basin.Seat.ITouchChrome.Release(int id, uint timeMs, double x, double y)
    {
        if (_touchFramePress is { } held)
        {
            _touchFramePress = null;
            held.Frame.TouchUp(x - held.Owner.X, y - held.Owner.Y, id);
            if (held.Frame.IsMenuOpen)
            {
                _openMenu = held.Frame;
            }
        }
    }

    void Basin.Seat.ITouchChrome.Cancel()
    {
        if (_touchFramePress is { } held)
        {
            _touchFramePress = null;
            held.Frame.TouchCancel();
        }
    }

    private void EndTouchDrag()
    {
        if (_touchFramePress is { } dragging)
        {
            _touchFramePress = null;
            dragging.Frame.TouchCancel();
        }

        if (_mode != DragMode.None)
        {
            if (_mode == DragMode.Split)
            {
                EndSplitDrag();
            }

            if (_mode == DragMode.Move && _grabWindow is { } dropped)
            {
                ReassignDraggedWorkspace(dropped);
            }

            _grabWindow?.SetResizing(false);
            _mode = DragMode.None;
            _effects.OnGrabEnd();
            _grabWindow = null;
        }
    }

    private (Frame Frame, IGrabTarget Owner)? FindFrame(SceneNode node)
    {
        foreach (var window in _windows)
        {
            if (window.Frame is { } frame && frame.OwnsNode(node))
            {
                return (frame, window);
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Frame is { } frame && frame.OwnsNode(node))
            {
                return (frame, xwindow);
            }
        }

        return null;
    }

    private void FocusFrameOwner(IGrabTarget owner)
    {
        if (owner is Window window)
        {
            FocusWindow(window);
        }
        else if (owner is XWindow xwindow)
        {
            FocusXWindow(xwindow);
        }
    }

    private void FocusSurfaceOwner(Surface surface)
    {
        foreach (var window in _windows)
        {
            if (window.Owns(surface))
            {
                FocusWindow(window);
                return;
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (xwindow.Framable && xwindow.XWin.Surface == surface)
            {
                FocusXWindow(xwindow);
                return;
            }
        }
    }

    private void OnPointerPlaced(uint time)
    {
        MoveCursor(_pointer!.X, _pointer.Y, time);
    }

    private void LoadCursorTheme()
    {
        _touchBinder!.EnsureCursorLoaded();
        BasinReport.Line($"CURSOR left_ptr {_cursor.Images?.Size ?? 0}px {_cursor.DrawnBy}");
    }

    internal void SetHostChromeCursor(string name) => _cursor.ShowNamed(name);

    private static bool IsTrusted(WlClient client)
    {
        if (Basin.Desktop.SecurityContextManager.ContextOf(client) is not null)
        {
            return false;
        }

        return client.TryGetCredentials(out var credentials) && credentials.Uid == OwnUid;
    }

    private static readonly uint OwnUid = GetUid();

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    private static readonly RenderColor Background = new(0.09f, 0.1f, 0.12f, 1f);

    private void OnOutputChanged(OutputView view)
    {
        var mode = view.Output.CurrentMode;
        var resized = view.Width != mode.Width || view.Height != mode.Height;
        if (!resized)
        {
            return;
        }

        (view.Width, view.Height) = (mode.Width, mode.Height);
        if ((view.Allocator ?? _allocator) is { } allocator)
        {
            if (view.Swapchain is null)
            {
                view.Swapchain = new Swapchain(allocator, mode.Width, mode.Height, _swapFormat, view.SwapModifiers ?? _swapModifiers);
            }
            else
            {
                view.Swapchain.Resize(mode.Width, mode.Height);
            }
        }
        else
        {
            view.Target?.Destroy();
            view.Target = new MemoryBuffer(mode.Width, mode.Height, DrmFormat.Xrgb8888);
        }

        Relayout();
        ReapplyPinnedGeometry();
    }

    private void ReapplyPinnedGeometry()
    {
        foreach (var window in _windows)
        {
            window.ReapplyPinnedGeometry();
        }

        foreach (var xwindow in _xwindows)
        {
            xwindow.ReapplyPinnedGeometry();
        }
    }

    private SceneTree ManagedXParent(Basin.XWayland.XWaylandWindow window)
    {
        if (window.X == 0 && window.Y == 0)
        {
            var slot = (_windows.Count + _xwindows.Count) % 8;
            window.Configure(60 + slot * 30, 60 + slot * 30, window.Width, window.Height);
        }

        _pendingXWorkspace = CurrentWorkspace();
        return new SceneTree(_pendingXWorkspace?.Tree ?? _layers.Windows);
    }

    private Workspace? _pendingXWorkspace;

    private void OnXAdopted(Basin.XWayland.XWaylandWindow window, SceneSurface scene, bool managed)
    {
        var xwindow = new XWindow(this, window, scene, framable: managed);
        if (managed)
        {
            xwindow.Workspace = _pendingXWorkspace;
            _pendingXWorkspace = null;
        }

        _xwindows.Add(xwindow);
        window.GeometryChanged += xwindow.Layout;
        if (managed)
        {
            window.DecorationsChanged += xwindow.UpdateDecorations;
            FocusXWindow(xwindow);
        }
    }

    private void OnXRemoved(Basin.XWayland.XWaylandWindow window, SceneSurface scene)
    {
        foreach (var xwindow in _xwindows.ToArray())
        {
            if (ReferenceEquals(xwindow.XWin, window))
            {
                RemoveXWindow(xwindow);
            }
        }
    }

    private void RemoveXWindow(XWindow xwindow)
    {
        if (!_xwindows.Remove(xwindow))
        {
            return;
        }

        xwindow.Destroy();
        if (_focusedX == xwindow)
        {
            _focusedX = null;
        }

        if (_grabWindow == xwindow)
        {
            _mode = DragMode.None;
            _effects.OnGrabEnd();
            _grabWindow = null;
        }

        xwindow.Workspace = null;
        _workspaceModel.RaiseMembersChanged();
        DropSwitcherCard(xwindow);
    }

    private void FocusXWindow(XWindow xwindow)
    {
        if (_focused is not null)
        {
            FocusWindow(null);
        }

        if (_focusedX != xwindow)
        {
            DismissOpenMenu();
            _focusedX?.SetDecorationFocus(false);
            _focusedX = xwindow;
            xwindow.SetDecorationFocus(true);
        }

        if (xwindow.Workspace is { } workspace)
        {
            workspace.LastFocused = xwindow;
        }

        xwindow.XWin.Activate();
        xwindow.XWin.Raise();
        xwindow.Tree.RaiseToTop();
        _stack.RaiseChanged();
        if (xwindow.XWin.Surface is { } surface)
        {
            _seat.Keyboard.NotifyEnter(surface);
            _textInput.NotifyFocus(surface);
        }
    }

    private void ActivateXWindow(Basin.XWayland.XWaylandWindow window)
    {
        foreach (var xwindow in _xwindows)
        {
            if (xwindow.XWin == window)
            {
                if (xwindow.Workspace is { } workspace && ViewOf(workspace) is { } view && view.Active != workspace)
                {
                    MarkUrgent(workspace);
                }
                else
                {
                    FocusXWindow(xwindow);
                }

                return;
            }
        }
    }

    private void Relayout()
    {
        var edge = 0;
        var row = 0;
        foreach (var view in _views)
        {
            if (view.AutoLayout || !_layout.Contains(view.Output))
            {
                continue;
            }

            var pinned = _layout.BoxOf(view.Output);
            if (pinned.Right > edge)
            {
                (edge, row) = (pinned.Right, pinned.Y);
            }
        }

        foreach (var view in _views)
        {
            if (!view.AutoLayout || !_layout.Contains(view.Output))
            {
                continue;
            }

            _layout.Move(view.Output, edge, row);
            edge += view.Output.LogicalSize().Width;
        }
    }

    private SceneRenderOptions SceneOptions(double scale) => new()
    {
        Background = Background,
        Scale = scale,
    };

    private SceneRenderOptions SceneOptions(IOutput output) => new()
    {
        Background = Background,
        Projection = OutputProjection.For(output),
    };

    private void RenderOutput(OutputView view)
    {
        _frameClock.BeginFrameAtNextRefresh(view.Output);
        var target = view.Swapchain?.Acquire(out _) ?? (IBuffer?)view.Target;
        if (target is null)
        {
            return;
        }

        var box = _layout.BoxOf(view.Output);
        _scene.Root.SetPosition(-box.X, -box.Y);
        if (!_scene.Render(_renderer, target, SceneOptions(view.Output)))
        {
            BasinReport.Line($"SHOT render failed");
        }

        MaybeScreenshot(view);
        _scene.Root.SetPosition(0, 0);
        _frameState.Clear();
        view.Output.Commit(_frameState.SetBuffer(target));

        view.Swapchain?.Presented(target);
        view.LastPresentedBuffer = target;

        _scene.SendFrameDone((uint)Environment.TickCount);
    }

    private readonly bool _useTransactions;
    private Transaction? _splitTransaction;
    private readonly List<SceneSnapshot> _splitSnapshots = [];

    private static int SplitX(Workspace workspace) =>
        workspace.TileArea.X + (int)(workspace.TileArea.Width * workspace.SplitFraction);

    internal void TileWindows()
    {
        if (CurrentWorkspace() is not { } workspace)
        {
            return;
        }

        workspace.Tiled.Clear();
        foreach (var window in _windows)
        {
            if (window.Workspace == workspace && !window.Minimized && workspace.Tiled.Count < 2)
            {
                workspace.Tiled.Add(window);
            }
        }

        if (workspace.Tiled.Count < 2)
        {
            workspace.Tiled.Clear();
            BasinReport.Line($"TILE needs two windows");
            return;
        }

        var view = _views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? _views[0];
        var origin = _layout.BoxOf(view.Output);
        var usable = view.UsableArea.IsEmpty ? origin with { X = 0, Y = 0 } : view.UsableArea;
        workspace.TileArea = new Box(origin.X + usable.X, origin.Y + usable.Y, usable.Width, usable.Height);
        workspace.SplitFraction = 0.5;
        ApplySplit(workspace);
        BasinReport.Line($"TILED {workspace.Tiled[0].Toplevel.AppId} | {workspace.Tiled[1].Toplevel.AppId} transactions={_useTransactions}");
    }

    internal void SetSplit(double fraction)
    {
        if (CurrentWorkspace() is not { Tiled.Count: 2 } workspace)
        {
            BasinReport.Line($"SPLIT needs a tiled pair");
            return;
        }

        workspace.SplitFraction = Math.Clamp(fraction, 0.1, 0.9);
        ApplySplit(workspace);
    }

    private void BeginSplitDrag(Workspace workspace)
    {
        _mode = DragMode.Split;
        _grabWindow = null;
        _splitWorkspace = workspace;
        _seat.Pointer.NotifyClearFocus();
        foreach (var window in workspace.Tiled)
        {
            window.SetResizing(true);
        }
    }

    private void DragSplit(double x)
    {
        if (_splitWorkspace is not { Tiled.Count: 2 } workspace || workspace.TileArea.Width <= 0)
        {
            return;
        }

        workspace.SplitFraction = Math.Clamp((x - workspace.TileArea.X) / workspace.TileArea.Width, 0.1, 0.9);
        ApplySplit(workspace);
    }

    private void EndSplitDrag()
    {
        if (_splitWorkspace is not { } workspace)
        {
            return;
        }

        foreach (var window in workspace.Tiled)
        {
            window.SetResizing(false);
        }
    }

    private void ApplySplit(Workspace workspace)
    {
        if (workspace.Tiled.Count != 2)
        {
            return;
        }

        var left = workspace.Tiled[0];
        var right = workspace.Tiled[1];
        var splitX = SplitX(workspace);
        var leftWidth = Math.Max(32, splitX - workspace.TileArea.X);
        var rightWidth = Math.Max(32, workspace.TileArea.Right - splitX);

        if (!_useTransactions)
        {
            left.MoveTo(workspace.TileArea.X, workspace.TileArea.Y);
            left.Toplevel.SetSize(leftWidth, workspace.TileArea.Height);
            right.MoveTo(splitX, workspace.TileArea.Y);
            right.Toplevel.SetSize(rightWidth, workspace.TileArea.Height);
            return;
        }

        DropSplitTransaction();
        var transaction = new Transaction(_loop);
        _splitTransaction = transaction;
        _splitWorkspace = workspace;

        FreezeForSplit(left);
        FreezeForSplit(right);

        left.MoveTo(workspace.TileArea.X, workspace.TileArea.Y);
        left.Toplevel.SetSize(leftWidth, workspace.TileArea.Height);
        left.Toplevel.SendConfigure(transaction);

        right.MoveTo(splitX, workspace.TileArea.Y);
        right.Toplevel.SetSize(rightWidth, workspace.TileArea.Height);
        right.Toplevel.SendConfigure(transaction);

        transaction.Completed += () =>
        {
            if (ReferenceEquals(_splitTransaction, transaction))
            {
                ThawAfterSplit();
                _splitTransaction = null;
                _loop.DeferDestroy(transaction);
            }
        };
        transaction.Seal();
    }

    private void FreezeForSplit(Window window)
    {
        if (window.SceneSurface is not { } scene || window.Tree is null)
        {
            return;
        }

        var snapshot = SceneSnapshot.Capture(window.Tree, window.Workspace?.Tree ?? _layers.Windows);
        _splitSnapshots.Add(snapshot);
        window.Tree.Enabled = false;

        scene.SendFrameDone((uint)Environment.TickCount);
    }

    private void ThawAfterSplit()
    {
        foreach (var window in _splitWorkspace?.Tiled ?? [])
        {
            if (window.Tree is not null)
            {
                window.Tree.Enabled = !window.Minimized;
            }
        }

        foreach (var snapshot in _splitSnapshots)
        {
            _loop.DeferDestroy(snapshot);
        }

        _splitSnapshots.Clear();
    }

    private void DropSplitTransaction()
    {
        if (_splitTransaction is not { } outstanding)
        {
            return;
        }

        _splitTransaction = null;
        ThawAfterSplit();
        _loop.DeferDestroy(outstanding);
    }

    private readonly Dictionary<XdgToplevelWindow, ToplevelRestore> _restoring = [];
    private readonly Dictionary<XdgToplevelWindow, Basin.Capabilities.ToplevelSessionState> _saved = [];

    private void WireSessions()
    {
        var sessions = _services.Require<Basin.Desktop.SessionManager>();
        sessions.ToplevelAdded += (session, name, toplevel) =>
        {
            _sessionWindows[toplevel] = (session, name);

            toplevel.Restored += restore => _restoring[toplevel] = restore;
            toplevel.Xdg.Committed += () => SaveSession(toplevel);
            toplevel.Destroyed += () =>
            {
                _sessionWindows.Remove(toplevel);
                _restoring.Remove(toplevel);
                _saved.Remove(toplevel);
            };
        };
    }

    private void SaveSession(XdgToplevelWindow toplevel)
    {
        if (!_sessionWindows.TryGetValue(toplevel, out var key) ||
            _windows.Find(w => w.Toplevel == toplevel) is not { } window)
        {
            return;
        }

        var (width, height) = window.GeometrySize;
        var state = new Basin.Capabilities.ToplevelSessionState
        {
            Geometry = new Box(window.X, window.Y, width, height),
            States = toplevel.SessionStates,
            OutputLayoutId = _layout.Id,
            WorkspaceName = window.Workspace?.Name,
        };

        if (_saved.TryGetValue(toplevel, out var previous) && previous == state)
        {
            return;
        }

        _saved[toplevel] = state;
        _sessionStore.Save(key.Session, key.Name, state);
    }

    private bool RestorePosition(Window window)
    {
        if (!_restoring.Remove(window.Toplevel, out var restore))
        {
            return false;
        }

        if (!restore.State.CanRestorePosition(_layout.Id))
        {
            BasinReport.Line($"RESTORE {restore.Name}: outputs moved, placing fresh");
            return false;
        }

        window.MoveTo(restore.State.Geometry.X, restore.State.Geometry.Y);
        if (restore.State.WorkspaceName is { } workspaceName)
        {
            RestoreWorkspace(window, restore.State.Geometry, workspaceName);
        }

        BasinReport.Line($"RESTORE {restore.Name} at {restore.State.Geometry.X},{restore.State.Geometry.Y}");
        return true;
    }

    private void RestoreWorkspace(Window window, Box geometry, string name)
    {
        var output = _layout.OutputAt(geometry.X + (geometry.Width / 2.0), geometry.Y + (geometry.Height / 2.0));
        var view = _views.FirstOrDefault(v => v.Output == output) ?? ViewAtCursor();
        if (view is null)
        {
            return;
        }

        var target = view.Workspaces.FirstOrDefault(ws => ws.Name == name)
            ?? CreateWorkspace(view, name, afterActive: false);
        if (window.Workspace != target)
        {
            MoveWindowToWorkspace(window, target);
            BasinReport.Line($"RESTORE workspace {name}");
        }
    }

    internal void PlaceCascade(IGrabTarget window)
    {
        var view = _views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? _views[0];
        var origin = _layout.BoxOf(view.Output);
        var usable = view.UsableArea.IsEmpty ? origin with { X = 0, Y = 0 } : view.UsableArea;
        var slot = _windows.Count % 8;
        window.MoveTo(origin.X + usable.X + 40 + (slot * 30), origin.Y + usable.Y + 40 + (slot * 30));
    }

    internal void OnWindowMapped(Window window)
    {
        _windows.Add(window);
        window.Minimized = false;
        _xdgToplevels.SetMinimized(window.Toplevel, false);

        var placed = RestorePosition(window);
        if (!placed &&
            !window.Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen) &&
            !window.Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized))
        {
            PlaceCascade(window);
        }

        if (window.Workspace is { } mapped && ViewOf(mapped)?.Active != mapped)
        {
            BasinReport.Line($"MAPPED {window.Toplevel.AppId} hidden");
        }
        else
        {
            FocusWindow(window);
            BasinReport.Line($"MAPPED {window.Toplevel.AppId}");
        }

        _workspaceModel.RaiseMembersChanged();
    }

    internal void OnWindowGone(Window window)
    {
        BasinReport.Line($"UNMAPPED {window.Toplevel.AppId}");
        _windows.Remove(window);
        var workspace = window.Workspace;
        window.Workspace = null;
        if (_focused == window)
        {
            if (workspace is not null && ViewOf(workspace)?.Active == workspace)
            {
                FocusWorkspaceWindow(workspace);
            }
            else
            {
                FocusWindow(null);
            }
        }

        if (_grabWindow == window)
        {
            _mode = DragMode.None;
            _effects.OnGrabEnd();
            _grabWindow = null;
        }

        if (workspace is not null && workspace.Tiled.Remove(window))
        {
            DissolveSplit(workspace);
        }

        _workspaceModel.RaiseMembersChanged();
        DropSwitcherCard(window);
    }

    private void FocusWindow(Window? window)
    {
        if (_focused == window)
        {
            return;
        }

        DismissOpenMenu();

        _focused?.Toplevel.SetActivated(false);
        _focused?.SetDecorationFocus(false);
        _focused = window;
        if (window is not null)
        {
            _focusedX?.SetDecorationFocus(false);
            _focusedX = null;
            if (window.Workspace is { } workspace)
            {
                workspace.LastFocused = window;
            }

            window.Toplevel.SetActivated(true);
            window.SetDecorationFocus(true);
            window.Tree?.RaiseToTop();
            _stack.RaiseChanged();
            _seat.Keyboard.NotifyEnter(window.Toplevel.Surface);
            _textInput.NotifyFocus(window.Toplevel.Surface);
        }
        else
        {
            _seat.Keyboard.NotifyClearFocus();
            _textInput.NotifyFocus(null);
        }
    }

    internal void SetMinimized(Window window, bool minimized)
    {
        if (window.Minimized == minimized)
        {
            return;
        }

        window.Minimized = minimized;
        if (window.Tree is { } tree)
        {
            tree.Enabled = !minimized;
        }

        _xdgToplevels.SetMinimized(window.Toplevel, minimized);
        if (minimized)
        {
            if (_focused == window)
            {
                if (window.Workspace is { } workspace && ViewOf(workspace)?.Active == workspace)
                {
                    FocusWorkspaceWindow(workspace);
                }
                else
                {
                    FocusWindow(null);
                }
            }
        }
        else
        {
            FocusWindow(window);
        }

        _workspaceModel.RaiseMembersChanged();
    }

    internal void BeginMove(IGrabTarget window, uint? serial = null)
    {
        if (TraceEnabled)
        {
            Trace($"beginmove win=({window.X},{window.Y}) cursor=({_cursorX:F0},{_cursorY:F0})");
        }

        _mode = DragMode.Move;
        _grabWindow = window;
        var (x, y) = _grabOrigin.For(serial);
        _grabX = x - window.X;
        _grabY = y - window.Y;
        _effects.OnMoveGrab(window.EffectTree, _grabX, _grabY);
    }

    internal void BeginResize(IGrabTarget window, ResizeEdges edges, uint? serial = null)
    {
        _mode = DragMode.Resize;
        _grabWindow = window;
        _grabEdges = edges;
        (_grabX, _grabY) = _grabOrigin.For(serial);
        var (width, height) = window.GeometrySize;
        _grabStart = new Box(window.X, window.Y, width, height);
        window.SetResizing(true);
    }

    private void WirePopup(XdgPopupWindow popup)
    {
        if (popup.Parent is not null && Basin.Desktop.LayerShellSceneDriver.RootLayerOf(popup) is null)
        {
            _popupPlacer.Attach(
                popup,
                _layers.Top,
                origin: () => PopupContentOrigin(popup),
                constrainBox: () => _layout.BoxOf(_layout.OutputAt(_cursorX, _cursorY) ?? _views[0].Output));
        }

        popup.Xdg.Mapped += () =>
        {
            RefreshSurfaceLuts();
            var origin = ParentOrigin(popup);
            var output = _layout.OutputAt(origin.X + popup.Geometry.X, origin.Y + popup.Geometry.Y)
                ?? _views[0].Output;
            _fractionalScale.AnnounceScale(popup.Surface, output.Scale);
        };
    }

    private Point ParentOrigin(XdgPopupWindow popup)
    {
        var content = PopupContentOrigin(popup);
        var chain = Basin.Desktop.PopupPlacer.ChainOffset(popup);
        return new Point(content.X + chain.X, content.Y + chain.Y);
    }

    private Point PopupContentOrigin(XdgPopupWindow popup)
    {
        var x = 0;
        var y = 0;
        var last = popup;
        var xdg = popup.Parent;
        while (xdg is not null)
        {
            if (xdg.Role is XdgPopupWindow parentPopup)
            {
                last = parentPopup;
                xdg = parentPopup.Parent;
            }
            else
            {
                var geometry = xdg.EffectiveGeometry;
                x += geometry.X;
                y += geometry.Y;
                if (xdg.Role is XdgToplevelWindow toplevel && FindWindow(toplevel) is { } window)
                {
                    x += window.X;
                    y += window.Y;
                }

                return new Point(x, y);
            }
        }

        if (last.LayerParent is { } layerParent && _layerDriver.SceneOf(layerParent) is { } layerScene)
        {
            x += layerScene.Tree.X;
            y += layerScene.Tree.Y;
        }

        return new Point(x, y);
    }

    private XWindow? FindXWindow(Basin.XWayland.XWaylandWindow xwin)
    {
        foreach (var xwindow in _xwindows)
        {
            if (xwindow.XWin == xwin)
            {
                return xwindow;
            }
        }

        return null;
    }

    private Window? FindWindow(XdgToplevelWindow toplevel)
    {
        foreach (var window in _windows)
        {
            if (window.Toplevel == toplevel)
            {
                return window;
            }
        }

        return null;
    }

    private void ApplyBlur(SceneSurface scene)
    {
        if (_blurEffect is not null && _backgroundEffects.BlurRegionOf(scene.Surface) is { } region)
        {
            scene.Content.SetBackdropEffect(_blurEffect, region);
        }
    }

    private void ApplyCorners(SceneSurface scene)
    {
        if (_cornerShader is not null)
        {
            scene.Content.TextureShader = _cornerShader;
        }
    }

    private SceneSurface? SceneSurfaceOf(Surface surface)
    {
        if (FindWindow(surface) is { SceneSurface: { } windowScene })
        {
            return windowScene;
        }

        foreach (var (layer, scene) in _layerDriver.Surfaces)
        {
            if (layer.Surface == surface && scene is not null)
            {
                return scene;
            }
        }

        return null;
    }

    private Window? FindWindow(Surface surface)
    {
        foreach (var window in _windows)
        {
            if (window.Toplevel.Surface == surface)
            {
                return window;
            }
        }

        return null;
    }

    private void RecordDecorationPreference(Surface surface, bool serverSide)
    {
        if (_ssdPreference.TryAdd(surface, serverSide))
        {
            surface.Destroyed += () => _ssdPreference.Remove(surface);
        }
        else
        {
            _ssdPreference[surface] = serverSide;
        }

        FindWindow(surface)?.SetDecorated(serverSide);
    }

    internal bool IsServerDecorated(XdgToplevelWindow toplevel) =>
        _ssdPreference.TryGetValue(toplevel.Surface, out var serverSide)
            ? serverSide
            : _kdeDecorations.ModeOf(toplevel.Surface) == Basin.Desktop.KdeServerDecorationManager.DecorationMode.Server ||
                _decorations.ModeOf(toplevel) == DecorationMode.ServerSide;

    private void WireLayerShell()
    {
        _layerDriver = new Basin.Desktop.LayerShellSceneDriver(_layerShell, _layout, _layers)
        {
            DefaultOutput = _ => _views.Count > 0
                ? (_views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output) ?? _views[0]).Global
                : null,
        };
        _layerDriver.TrackPopups(_shell);
        _layerDriver.PopupSceneCreated += (_, _, _) => RefreshSurfaceLuts();
        _layerDriver.PopupBounds = _ =>
            _layout.BoxOf(_layout.OutputAt(_cursorX, _cursorY) ?? _views[0].Output);
        _layerDriver.SceneCreated += (layer, _) =>
        {
            RefreshSurfaceLuts();
            if (layer.KeyboardInteractivity != Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.KeyboardInteractivity.None)
            {
                _seat.Keyboard.NotifyEnter(layer.Surface);
            }
        };
        _layerDriver.Removed += _ =>
        {
            if (_focused is { } focused)
            {
                _seat.Keyboard.NotifyEnter(focused.Toplevel.Surface);
            }
        };
        _layerDriver.UsableAreaChanged += (output, usable) =>
        {
            foreach (var view in _views)
            {
                if (view.Output != output)
                {
                    continue;
                }

                view.UsableArea = usable;
                foreach (var (layer, _) in _layerDriver.Surfaces)
                {
                    if (layer.Output?.Output == output)
                    {
                        _fractionalScale.AnnounceScale(layer.Surface, view.Output.Scale);
                    }
                }
            }
        };
    }

    private void ArrangeLayerSurfaces() => _layerDriver.Rearrange();

    private void WireSessionLock()
    {
        _lockDriver = new Basin.Desktop.SessionLockSceneDriver(
            _sessionLock, _seat, _layers.Lock, _layout, _layers.SetLocked);
        _lockDriver.Locked += () => BasinReport.Line($"LOCKED");
        _lockDriver.Unlocked += () =>
        {
            if (_focused is { } focused)
            {
                _seat.Keyboard.NotifyEnter(focused.Toplevel.Surface);
            }

            BasinReport.Line($"UNLOCKED");
        };
        _lockDriver.Abandoned += () => BasinReport.Line($"LOCK ABANDONED (staying blanked)");
        _lockDriver.LockSurfaceAdded += (lockSurface, _) =>
        {
            RefreshSurfaceLuts();
            _fractionalScale.AnnounceScale(lockSurface.Surface, lockSurface.Output.Output.Scale);
        };
    }

    private void WirePointer(WaylandPointerDevice pointer)
    {
        _cursor.AttachParent(pointer);

        pointer.Enter += (output, x, y) =>
        {
            var (layoutX, layoutY) = _layout.ToLayout(output, x, y);
            MoveCursor(layoutX, layoutY, (uint)Environment.TickCount);
        };
        pointer.Motion += (time, x, y) =>
        {
            var output = _layout.OutputAt(_cursorX, _cursorY) ?? _views[0].Output;
            var (layoutX, layoutY) = _layout.ToLayout(output, x, y);
            MoveCursor(layoutX, layoutY, time);
        };
        pointer.RelativeMotion += (time, dx, dy, dxu, dyu) =>
            _relativePointer.NotifyMotion(time, dx, dy, dxu, dyu);
        pointer.Button += (time, button, pressed) => OnButton(time, button, pressed);
        pointer.Axis += (time, axis) => _seat.Pointer.NotifyAxis(time, axis);
        pointer.Leave += () => _seat.Pointer.NotifyClearFocus();
        pointer.SwipeBegin += (time, fingers) =>
        {
            if (!BeginWorkspaceSwipe(fingers, time))
            {
                _gestures.NotifySwipeBegin(time, fingers);
            }
        };
        pointer.SwipeUpdate += (time, dx, dy) =>
        {
            if (!UpdateWorkspaceSwipe(dx, dy, time))
            {
                _gestures.NotifySwipeUpdate(time, dx, dy);
            }
        };
        pointer.SwipeEnd += (time, cancelled) =>
        {
            if (!EndWorkspaceSwipe(cancelled, time))
            {
                _gestures.NotifySwipeEnd(time, cancelled);
            }
        };
        pointer.PinchBegin += (time, fingers) => _gestures.NotifyPinchBegin(time, fingers);
        pointer.PinchUpdate += (time, dx, dy, scale, rotation) =>
            _gestures.NotifyPinchUpdate(time, dx, dy, scale, rotation);
        pointer.PinchEnd += (time, cancelled) => _gestures.NotifyPinchEnd(time, cancelled);
        pointer.HoldBegin += (time, fingers) => _gestures.NotifyHoldBegin(time, fingers);
        pointer.HoldEnd += (time, cancelled) => _gestures.NotifyHoldEnd(time, cancelled);
    }

    internal void InjectPointerMotion(uint time, double dx, double dy) =>
        MoveCursor(_cursorX + dx, _cursorY + dy, time);

    internal void InjectPointerMotionAbsolute(uint time, double x, double y)
    {
        var bounds = _layout.Bounds;
        MoveCursor(bounds.X + (x * bounds.Width), bounds.Y + (y * bounds.Height), time);
    }

    internal void InjectPointerButton(uint time, uint button, bool pressed) => OnButton(time, button, pressed);

    internal void InjectKey(uint time, uint key, bool pressed) =>
        HandleKey(time, key, pressed, fromInputMethod: true);

    private void MoveCursor(double x, double y, uint time)
    {
        var rawDx = x - _lastRawX;
        var rawDy = y - _lastRawY;
        (_lastRawX, _lastRawY) = (x, y);
        if (ActiveLock() is not null)
        {
            _relativePointer.NotifyMotion((ulong)time * 1000, rawDx, rawDy, rawDx, rawDy);
            return;
        }

        _cursorX = x;
        _cursorY = y;
        _cursor.MoveTo(x, y);
        if (TraceEnabled)
        {
            Trace($"motion {x:F1},{y:F1} mode={_mode}");
        }

        if (_touchMoveResize is not { Dragging: true } && DragTo(x, y))
        {
            return;
        }

        UpdateHoverCursor(x, y);
        RouteMotion(time, x, y);
    }

    private void RefreshPointer()
    {
        if (_mode != DragMode.None || _touchMoveResize is { Dragging: true } || ActiveLock() is not null)
        {
            return;
        }

        UpdateHoverCursor(_cursorX, _cursorY);
        RouteMotion((uint)Environment.TickCount, _cursorX, _cursorY);
    }

    private bool DragTo(double x, double y)
    {
        switch (_mode)
        {
            case DragMode.Move when _grabWindow is { } window:
                var beforeX = window.X;
                var beforeY = window.Y;
                window.MoveTo((int)(x - _grabX), (int)(y - _grabY));
                _effects.OnMoved(window.X - beforeX, window.Y - beforeY);
                return true;

            case DragMode.Split:
                DragSplit(x);
                return true;

            case DragMode.Resize when _grabWindow is { } window:
                var box = new ResizeDrag(_grabEdges, _grabStart, _grabX, _grabY).BoxFor(x, y, window.X, window.Y);
                window.ResizeTo(box.X, box.Y, box.Width, box.Height, _grabEdges);
                return true;

            default:
                return false;
        }
    }

    private void SecondaryRepaint(OutputView view)
    {
        if (view.Swapchain is null || view.Swapchain.Acquire(out _) is not { } buffer)
        {
            return;
        }

        var replicating = view.ReplicaSource is not null;
        if (view.ReplicaSource is { } replicaSource)
        {
            if (!ReplicaBlit(replicaSource, buffer))
            {
                return;
            }
        }
        else
        {
            var box = _layout.BoxOf(view.Output);
            _scene.Root.SetPosition(-box.X, -box.Y);
            var rendered = _scene.Render(_renderer, buffer, SceneOptions(view.Output));
            _scene.Root.SetPosition(0, 0);
            if (!rendered)
            {
                return;
            }
        }

        _frameState.Clear();
        _frameState.SetBuffer(buffer);
        if (view.Output.Commit(_frameState))
        {
            view.Scheduler!.NotifyCommitted();
            view.LastPresentedBuffer = buffer;
        }

        _capture.NotifyDamaged(view.Output, new Box(0, 0, view.Width, view.Height));
        if (!replicating)
        {
            _scene.SendFrameDone((uint)Environment.TickCount);
        }
    }

    private bool ReplicaBlit(OutputView source, IBuffer target)
    {
        if ((source.LastPresentedBuffer ?? source.SceneOutput?.LastTarget) is not { } sourceBuffer)
        {
            return false;
        }

        if (_renderer.ImportTexture(sourceBuffer) is not { } texture)
        {
            return false;
        }

        try
        {
            var scale = Math.Min(
                (double)target.Width / sourceBuffer.Width, (double)target.Height / sourceBuffer.Height);
            var width = (int)Math.Round(sourceBuffer.Width * scale);
            var height = (int)Math.Round(sourceBuffer.Height * scale);
            var pass = _renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddRect(new RenderColor(0f, 0f, 0f, 1f), new Box(0, 0, target.Width, target.Height));
            pass.AddTexture(texture, new TextureRenderOptions
            {
                DstBox = new Box((target.Width - width) / 2, (target.Height - height) / 2, width, height),
            });
            pass.Submit();
            return true;
        }
        finally
        {
            texture.Dispose();
        }
    }

    private void RouteMotion(uint time, double x, double y)
    {
        _dragIcon.Follow();

        Window? dragged = null;
        if (_toplevelDrags.Attachment is { } attached && FindWindow(attached.Toplevel) is { Tree: not null } window)
        {
            var geometry = attached.Toplevel.Xdg.EffectiveGeometry;
            window.MoveTo((int)x - attached.OffsetX - geometry.X, (int)y - attached.OffsetY - geometry.Y);
            window.Tree!.Enabled = false;
            dragged = window;
        }

        var hit = _scene.SurfaceAt(x, y);
        if (dragged?.Tree is { } draggedTree)
        {
            draggedTree.Enabled = true;
        }

        _seat.Pointer.NotifyMotionAt(time, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, x, y);
        UpdateConstraint(hit?.Surface);
    }

    private Basin.Desktop.PointerConstraint? _activeConstraint;
    private WaylandOutput? _parentLockOutput;
    private Basin.Desktop.PointerConstraint? _parentLockConstraint;

    private Basin.Desktop.PointerConstraint? ActiveLock() =>
        _activeConstraint is { IsActive: true, Kind: Basin.Desktop.ConstraintKind.Lock } ? _activeConstraint : null;

    private void UpdateConstraint(Surface? focused)
    {
        var next = focused is null ? null : _constraints.ConstraintFor(focused);
        if (_activeConstraint == next)
        {
            return;
        }

        _activeConstraint?.Deactivate();
        _activeConstraint = next;
        next?.Activate();
        SyncParentPointerLock();
    }

    private void SyncParentPointerLock()
    {
        if (_backend is null)
        {
            return;
        }

        var wanted = ActiveLock() is null
            ? null
            : _layout.OutputAt(_cursorX, _cursorY) as WaylandOutput;
        if (ReferenceEquals(wanted, _parentLockOutput))
        {
            return;
        }

        if (_parentLockOutput is { } previous)
        {
            var (releaseX, releaseY) = ParentLockRelease();
            var box = _layout.BoxOf(previous);
            previous.SetCursorPositionHint(
                (releaseX - box.X) * previous.Scale,
                (releaseY - box.Y) * previous.Scale);
            previous.LockPointer(false);
            if (releaseX != _cursorX || releaseY != _cursorY)
            {
                MoveCursor(releaseX, releaseY, (uint)Environment.TickCount);
            }
        }

        _parentLockOutput = wanted;
        _parentLockConstraint = wanted is null ? null : ActiveLock();
        if (wanted is not null && !wanted.LockPointer(true))
        {
            _parentLockOutput = null;
            _parentLockConstraint = null;
        }
    }

    private void RaiseHostWindow(Window window)
    {
        if (_backend is null)
        {
            return;
        }

        var target = window.Workspace is { } workspace && ViewOf(workspace) is { } view
            ? view.Output as WaylandOutput
            : null;
        target ??= _backend.Outputs.Count > 0 ? _backend.Outputs[0] : null;
        target?.RequestActivation();
    }

    private (WaylandOutput Output, Box Rect)? LocateGuestCaret(Surface surface, Box rect)
    {
        _caretSurfaces.Clear();
        _scene.CollectSurfaces(_caretSurfaces);
        foreach (var entry in _caretSurfaces)
        {
            if (entry.Surface != surface)
            {
                continue;
            }

            var layoutX = entry.Box.X + rect.X;
            var layoutY = entry.Box.Y + rect.Y;
            if (_layout.OutputAt(layoutX, layoutY) is not WaylandOutput output)
            {
                return null;
            }

            var box = _layout.BoxOf(output);
            return (output, new Box(
                (int)Math.Round((layoutX - box.X) * output.Scale),
                (int)Math.Round((layoutY - box.Y) * output.Scale),
                (int)Math.Round(rect.Width * output.Scale),
                (int)Math.Round(rect.Height * output.Scale)));
        }

        return null;
    }

    private (double X, double Y) ParentLockRelease()
    {
        if (_parentLockConstraint?.CursorPositionHint is { } hint &&
            _scene.SurfaceAt(_cursorX, _cursorY) is { Surface: { } surface } at &&
            surface == _parentLockConstraint.Surface)
        {
            return (_cursorX - at.X + hint.X, _cursorY - at.Y + hint.Y);
        }

        return (_cursorX, _cursorY);
    }

    private void OnButton(uint time, uint button, bool pressed)
    {
        if (TraceEnabled)
        {
            Trace($"button {button} pressed={pressed} mode={_mode}");
        }

        if (_mode != DragMode.None)
        {
            if (_mode == DragMode.Split)
            {
                EndSplitDrag();
            }

            if (_mode == DragMode.Move && _grabWindow is { } dropped)
            {
                ReassignDraggedWorkspace(dropped);
            }

            _grabWindow?.SetResizing(false);
            _mode = DragMode.None;
            _effects.OnGrabEnd();
            _grabWindow = null;
            _touchMoveResize?.End();
            _framePress = null;
            if (!pressed)
            {
                _seat.Pointer.NotifyButton(time, button, pressed: false);
                RouteMotion(time, _cursorX, _cursorY);
                return;
            }

            RouteMotion(time, _cursorX, _cursorY);
        }

        if (_openMenu is { } menu)
        {
            var menuHit = _scene.NodeAt(_cursorX, _cursorY);
            if (menuHit is { Node: { } menuNode } && menu.OwnsMenuNode(menuNode))
            {
                if (button == InputCodes.BtnLeft)
                {
                    menu.MenuPointerButton(menuHit.Value.X, menuHit.Value.Y, pressed);
                    if (!menu.IsMenuOpen)
                    {
                        _openMenu = null;
                        _menuHovering = false;
                    }
                }

                return;
            }

            if (pressed)
            {
                DismissOpenMenu();
            }
        }

        if (button == InputCodes.BtnLeft && !pressed && _framePress is { } held)
        {
            _framePress = null;
            PrepareMenu(held);
            held.Frame.PointerButton(_cursorX - held.Owner.X, _cursorY - held.Owner.Y, pressed: false, time);
            if (held.Frame.IsMenuOpen)
            {
                _openMenu = held.Frame;
            }

            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            CurrentWorkspace() is { Tiled.Count: 2 } tiledWorkspace &&
            Math.Abs(_cursorX - SplitX(tiledWorkspace)) <= SplitGrabZone &&
            _cursorY >= tiledWorkspace.TileArea.Y && _cursorY < tiledWorkspace.TileArea.Bottom)
        {
            BeginSplitDrag(tiledWorkspace);
            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            _scene.NodeAt(_cursorX, _cursorY) is { Node: { } frameNode } &&
            FindFrame(frameNode) is { } frameHit &&
            frameHit.Frame.PartAt(_cursorX - frameHit.Owner.X, _cursorY - frameHit.Owner.Y) != FramePart.None)
        {
            FocusFrameOwner(frameHit.Owner);
            _framePress = frameHit;
            PrepareMenu(frameHit);
            frameHit.Frame.PointerButton(_cursorX - frameHit.Owner.X, _cursorY - frameHit.Owner.Y, pressed: true, time);
            return;
        }

        if (pressed && button == InputCodes.BtnRight && !_seat.Pointer.HasGrab &&
            _scene.NodeAt(_cursorX, _cursorY) is { Node: { } rightNode } &&
            FindFrame(rightNode) is { } rightHit)
        {
            var localX = _cursorX - rightHit.Owner.X;
            var localY = _cursorY - rightHit.Owner.Y;
            if (rightHit.Frame.PartAt(localX, localY) is FramePart.Title or FramePart.Icon)
            {
                FocusFrameOwner(rightHit.Owner);
                PrepareMenu(rightHit);
                rightHit.Frame.OpenMenu(localX, localY);
                _openMenu = rightHit.Frame.IsMenuOpen ? rightHit.Frame : null;
            }

            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            _scene.NodeAt(_cursorX, _cursorY) is null &&
            TryRingResize(_cursorX, _cursorY, RingMargin, CornerZone, out var ringEdges, out var ringWindow, out var ringXWindow))
        {
            if (ringWindow is not null)
            {
                FocusWindow(ringWindow);
                BeginResize(ringWindow, ringEdges);
            }
            else if (ringXWindow is not null)
            {
                FocusXWindow(ringXWindow);
                BeginResize(ringXWindow, ringEdges);
            }

            return;
        }

        if (pressed && button == InputCodes.BtnLeft && !_seat.Pointer.HasGrab &&
            _scene.SurfaceAt(_cursorX, _cursorY) is { Surface: { } surface })
        {
            foreach (var window in _windows)
            {
                if (window.Owns(surface))
                {
                    FocusWindow(window);
                    if (IsAltDown())
                    {
                        BeginMove(window);
                        return;
                    }

                    break;
                }
            }

            foreach (var xwindow in _xwindows)
            {
                if (xwindow.Framable && xwindow.XWin.Surface == surface)
                {
                    FocusXWindow(xwindow);
                    if (IsAltDown())
                    {
                        BeginMove(xwindow);
                        return;
                    }

                    break;
                }
            }
        }

        _seat.Pointer.NotifyButton(time, button, pressed);
    }

    private const int RingMargin = 16;

    private bool TryRingResize(double x, double y, int margin, int corner, out ResizeEdges edges, out Window? xdgWindow, out XWindow? xWindow)
    {
        foreach (var window in _windows)
        {
            if (window.Minimized)
            {
                continue;
            }

            if (ResizeRing.EdgesAt(window.FrameBox, x, y, margin, corner) is var e && e != ResizeEdges.None)
            {
                (edges, xdgWindow, xWindow) = (e, window, null);
                return true;
            }
        }

        foreach (var xwindow in _xwindows)
        {
            if (ResizeRing.EdgesAt(xwindow.FrameBox, x, y, margin, corner) is var e && e != ResizeEdges.None)
            {
                (edges, xdgWindow, xWindow) = (e, null, xwindow);
                return true;
            }
        }

        (edges, xdgWindow, xWindow) = (ResizeEdges.None, null, null);
        return false;
    }

    private void UpdateHoverCursor(double x, double y)
    {
        var hit = _scene.NodeAt(x, y);
        var surface = hit?.Surface;

        if (_openMenu is { } openMenu && hit is { Node: { } maybeMenu } && openMenu.OwnsMenuNode(maybeMenu))
        {
            LeaveFrameHover(except: null);
            _cursor.SetHover(surface, overClient: false);
            _menuHovering = true;
            openMenu.MenuPointerMotion(hit.Value.X, hit.Value.Y);
            _cursor.ShowNamed("left_ptr");
            return;
        }

        if (_menuHovering)
        {
            _menuHovering = false;
            _openMenu?.MenuPointerLeave();
        }

        if (surface is not null)
        {
            LeaveFrameHover(except: null);
            _cursor.SetHover(surface, overClient: true);
            return;
        }

        _cursor.SetHover(null, overClient: false);
        if (hit is { Node: { } hoverNode } && FindFrame(hoverNode) is { } frameHover)
        {
            LeaveFrameHover(except: frameHover.Frame);
            _frameHover = frameHover;
            var localX = x - frameHover.Owner.X;
            var localY = y - frameHover.Owner.Y;
            frameHover.Frame.PointerMotion(localX, localY);
            _cursor.ShowNamed(frameHover.Frame.CursorAt(localX, localY) ?? "left_ptr");
            return;
        }

        LeaveFrameHover(except: null);

        if (hit is null && TryRingResize(x, y, RingMargin, CornerZone, out var edges, out _, out _))
        {
            _cursor.ShowNamed(ResizeRing.CursorFor(edges));
            return;
        }

        _cursor.ShowNamed("left_ptr");
    }

    private (Frame Frame, IGrabTarget Owner)? _frameHover;
    private (Frame Frame, IGrabTarget Owner)? _framePress;

    private Frame? _openMenu;
    private bool _menuHovering;

    private void PrepareMenu((Frame Frame, IGrabTarget Owner) hit)
    {
        hit.Frame.MenuOrigin = new Point(hit.Owner.X, hit.Owner.Y);
        var output = _layout.OutputAt(_cursorX, _cursorY) ?? _views.FirstOrDefault()?.Output;
        hit.Frame.MenuConstraint = output is null ? default : _layout.BoxOf(output);
    }

    private void DismissOpenMenu()
    {
        _openMenu?.DismissMenu();
        _openMenu = null;
        _menuHovering = false;
    }

    private void LeaveFrameHover(Frame? except)
    {
        if (_frameHover is { } hover && hover.Frame != except)
        {
            hover.Frame.PointerLeave();
            _frameHover = null;
        }
    }

    internal double ScaleAt(double x, double y)
    {
        var view = _views.FirstOrDefault(v => _layout.OutputAt(x, y) == v.Output) ?? _views.FirstOrDefault();
        return view?.Output.Scale ?? 1.0;
    }

    internal double ScaleForBox(in Box box)
    {
        var best = 0.0;
        foreach (var view in _views)
        {
            if (!_layout.BoxOf(view.Output).Intersect(box).IsEmpty)
            {
                best = Math.Max(best, view.Output.Scale);
            }
        }

        return best > 0 ? best : ScaleAt(box.X, box.Y);
    }

    internal double ScaleForWindow(Window window) => ScaleForBox(window.ScaleBox);

    private void WireKeyboard(WaylandKeyboardDevice keyboard)
    {
        keyboard.Keymap += bytes => _seat.Keyboard.SetKeymapFromBuffer(bytes);
        keyboard.RepeatInfo += (rate, delay) => _seat.Keyboard.SetRepeatInfo(rate, delay);
        keyboard.Modifiers += (d, l, k, g) =>
        {
            _seat.Keyboard.Activate(null);
            _seat.Keyboard.NotifyModifiers(d, l, k, g);
        };
        keyboard.Key += (time, key, pressed) =>
        {
            _seat.Keyboard.Activate(null);
            HandleKey(time, key, pressed);
        };
    }

    private void HandleKey(uint time, uint key, bool pressed, bool fromInputMethod = false)
    {
        if (pressed && key == InputCodes.KeyEsc && _openMenu is not null)
        {
            DismissOpenMenu();
            return;
        }

        if (pressed && !_sessionLock.IsLocked && !_shortcutsInhibit.IsActive(_seat.Keyboard.Focus)
            && IsAltDown() && HandleKeybind(key))
        {
            return;
        }

        if (_effects.SwitcherActive && !_sessionLock.IsLocked)
        {
            if (!pressed && key is InputCodes.KeyLeftAlt or InputCodes.KeyRightAlt)
            {
                EndSwitcher(focus: true);
            }
            else
            {
                if (pressed && key == InputCodes.KeyEsc)
                {
                    EndSwitcher(focus: false);
                }

                return;
            }
        }

        if (!fromInputMethod && _textInput.HasKeyboardGrab)
        {
            _textInput.ForwardKey(time, key, pressed);
            return;
        }

        _seat.Keyboard.NotifyKey(time, key, pressed);
    }

    private void WireStdin() => _stdinCommands = new StdinCommands(_loop, HandleCommand);

    private void HandleCommand(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case ["move", var x, var y]:
                MoveCursor(double.Parse(x), double.Parse(y), (uint)Environment.TickCount);
                break;
            case ["button", var code, var state]:
                OnButton((uint)Environment.TickCount, uint.Parse(code), state == "1");
                break;
            case ["key", var code, var state]:
                HandleKey((uint)Environment.TickCount, uint.Parse(code), state == "1");
                break;
            case ["shot", var path]:
                _shotPath = path;
                _shotView = 0;
                RenderOutput(_views[0]);
                break;
            case ["shot", var path, var index]:
                _shotPath = path;
                _shotView = int.Parse(index);
                RenderOutput(_views[_shotView]);
                break;
            case ["scale", var viewIndex, var factor]:
                SetOutputScale(_views[int.Parse(viewIndex)], double.Parse(factor));
                break;
            case ["shotraw", var path]:
                DumpPresented(_views[0], path);
                break;
            case ["where"]:
                foreach (var window in _windows)
                {
                    BasinReport.Line($"WIN {window.Toplevel.AppId} {window.X} {window.Y} mode={_mode} scene={(window.SceneSurface is null ? "none" : "yes")}");
                }

                break;
            case ["clip", var index, var cx, var cy, var cw, var ch]:
                {
                    var target = _windows[int.Parse(index)];
                    if (target.Tree is not null)
                    {
                        target.Tree.ClipBox = new Box(int.Parse(cx), int.Parse(cy), int.Parse(cw), int.Parse(ch));
                        BasinReport.Line($"CLIP {index} {target.Tree.ClipBox}");
                    }
                }

                break;
            case ["tile"]:
                TileWindows();
                break;
            case ["ws"]:
                PrintWorkspaces();
                break;
            case ["ws", "next"]:
                SwitchWorkspace(1);
                break;
            case ["ws", "prev"]:
                SwitchWorkspace(-1);
                break;
            case ["ws", "create"]:
                if (ViewAtCursor() is { } createView)
                {
                    ActivateWorkspace(createView, CreateWorkspace(createView, null, afterActive: true));
                }

                break;
            case ["ws", "create", var wsName]:
                if (ViewAtCursor() is { } namedView)
                {
                    ActivateWorkspace(namedView, CreateWorkspace(namedView, wsName, afterActive: true));
                }

                break;
            case ["ws", "move"]:
                CarryFocusedWindow(1);
                break;
            case ["split", var fraction]:
                SetSplit(double.Parse(fraction));
                break;
            case ["dumpscene"]:
                DumpTree(_scene.Root, 0);
                break;
            case ["stats"]:
                BasinReport.Line($"STATS transactions={_useTransactions} timedout={Transaction.TimedOutCount}");
                BasinReport.Line($"STATS cursor theme={(_cursor.Images?.HasTheme == true ? _cursor.Images.Size.ToString() : "none")} " + $"showing={_cursor.Showing} " + $"on={(_cursor.CursorOutput?.Name ?? "none")}");
                foreach (var view in _views)
                {
                    var so = view.SceneOutput;
                    BasinReport.Line(so is null
                        ? $"STATS {view.Output.Name} full-repaint scale={view.Output.Scale}"
                        : $"STATS {view.Output.Name} scanout={so.ScanoutCommits} composed={so.ComposedCommits} skipped={so.SkippedCommits} direct={so.IsDirectScanout} offload={so.OffloadedLayers}/{so.OffloadCommits} swcursor={_cursor.IsSoftwareOn(view.Output)} scale={view.Output.Scale}");
                    if (so is not null)
                    {
                        foreach (var reason in Enum.GetValues<Basin.Scene.PlaneDeclineReason>())
                        {
                            if (so.DeclinedFor(reason) > 0)
                            {
                                BasinReport.Line($"STATS   declined {reason} {so.DeclinedFor(reason)}");
                            }
                        }
                    }
                }

                break;
            case ["nightlight", "off"]:
                ApplyNightLight(null);
                BasinReport.Line($"NIGHTLIGHT off");
                break;
            case ["nightlight", var kelvin]:
                ApplyNightLight(double.Parse(kelvin));
                BasinReport.Line($"NIGHTLIGHT {kelvin}K");
                break;
            case ["gc"]:
                var now = GC.GetAllocatedBytesForCurrentThread();
                BasinReport.Line($"GC {now - _gcMark} bytes since last mark");
                _gcMark = now;
                break;
            case ["quit"]:
                _runLoop.Stop();
                break;
        }
    }

    private string? _shotPath;
    private int _shotView;
    private long _gcMark;

    private void DumpPresented(OutputView view, string path)
    {
        BasinReport.Line(SceneScreenshot.WritePresented(view.LastPresentedBuffer, _renderer, path) switch
        {
            ScreenshotOutcome.NoFrame => "SHOTRAW unavailable (nothing presented yet)",
            ScreenshotOutcome.Unreadable => "SHOTRAW unavailable (presented buffer not importable)",
            _ => $"SHOTRAW {path}",
        });
    }

    private void MaybeScreenshot(OutputView view)
    {
        if (_shotPath is null || view != _views[_shotView])
        {
            return;
        }

        var path = _shotPath;
        _shotPath = null;
        if (SceneScreenshot.Write(_scene, _renderer, path, view.Width, view.Height, SceneOptions(view.Output)))
        {
            BasinReport.Line($"SHOT {path}");
        }
    }

    private bool IsAltDown() => _seat.Keyboard.State?.IsModActive("Mod1") == true;

    private bool IsShiftDown() => _seat.Keyboard.State?.IsModActive("Shift") == true;

    private bool HandleKeybind(uint key)
    {
        switch (key)
        {
            case InputCodes.KeyEsc:
                _runLoop.Stop();
                return true;

            case InputCodes.KeyEnter:
                try
                {
                    Basin.Diagnostics.BasinDiagnostics.StartClient("foot", _socket)?.Dispose();
                }
                catch (Exception e)
                {
                    _log.Error($"spawn failed: {e.Message}");
                }

                return true;

            case InputCodes.KeyTab when _effects.SwitcherEnabled &&
                (_effects.SwitcherActive || (CurrentWorkspace() is { } cards && WorkspaceWindowCount(cards) > 1)):
                AdvanceSwitcher();
                return true;

            case InputCodes.KeyTab when CurrentWorkspace() is { } cycle && WorkspaceWindowCount(cycle) > 1:
                CycleWorkspaceFocus(cycle);
                return true;

            case InputCodes.KeyS:
                CycleScale();
                return true;

            case InputCodes.KeyRight when IsShiftDown():
                CarryFocusedWindow(1);
                return true;

            case InputCodes.KeyLeft when IsShiftDown():
                CarryFocusedWindow(-1);
                return true;

            case InputCodes.KeyRight:
                SwitchWorkspace(1);
                return true;

            case InputCodes.KeyLeft:
                SwitchWorkspace(-1);
                return true;

            case InputCodes.KeyN when ViewAtCursor() is { } view:
                ActivateWorkspace(view, CreateWorkspace(view, null, afterActive: true));
                return true;

            default:
                return false;
        }
    }

    private void CycleWorkspaceFocus(Workspace workspace)
    {
        var members = new List<Window>();
        foreach (var window in _windows)
        {
            if (window.Workspace == workspace && !window.Minimized)
            {
                members.Add(window);
            }
        }

        if (members.Count == 0)
        {
            return;
        }

        var index = _focused is null ? 0 : (members.IndexOf(_focused) + 1) % members.Count;
        FocusWindow(members[index]);
    }

    private readonly List<IGrabTarget> _switcherWindows = [];
    private SceneRect? _switcherDim;

    private bool SwitcherCardLive(IGrabTarget card) => card switch
    {
        Window window => _windows.Contains(window) && window.Tree is { IsDestroyed: false },
        XWindow xwindow => _xwindows.Contains(xwindow) && !xwindow.Tree.IsDestroyed,
        _ => false,
    };

    private void AdvanceSwitcher()
    {
        if (!_effects.SwitcherActive)
        {
            var workspace = CurrentWorkspace();
            _switcherWindows.Clear();
            foreach (var window in _windows)
            {
                if (window.Workspace == workspace && !window.Minimized && window.Tree is { IsDestroyed: false })
                {
                    _switcherWindows.Add(window);
                }
            }

            foreach (var xwindow in _xwindows)
            {
                if (xwindow.Workspace == workspace && xwindow.Framable && !xwindow.Tree.IsDestroyed)
                {
                    _switcherWindows.Add(xwindow);
                }
            }

            if (_switcherWindows.Count < 2)
            {
                _switcherWindows.Clear();
                return;
            }

            var output = _layout.OutputAt(_cursorX, _cursorY) ?? _views[0].Output;
            var box = _layout.BoxOf(output);
            var focused = _focused is not null ? _switcherWindows.IndexOf(_focused)
                : _focusedX is not null ? _switcherWindows.IndexOf(_focusedX)
                : -1;
            var start = (focused + 1) % _switcherWindows.Count;
            _switcherDim = new SceneRect(workspace?.Tree ?? _layers.Windows, box.Width, box.Height, new RenderColor(0f, 0f, 0f, 0.45f));
            _switcherDim.SetPosition(box.X, box.Y);
            var trees = new List<SceneTree>(_switcherWindows.Count);
            foreach (var card in _switcherWindows)
            {
                trees.Add(card.EffectTree!);
            }

            _effects.SwitcherBegin(trees, box, start);
            RestackSwitcher();
            return;
        }

        var next = _effects.SwitcherSelected;
        for (var step = 0; step < _switcherWindows.Count; step++)
        {
            next = (next + 1) % _switcherWindows.Count;
            if (SwitcherCardLive(_switcherWindows[next]))
            {
                break;
            }
        }

        _effects.SwitcherSelect(next);
        RestackSwitcher();
    }

    private void RestackSwitcher()
    {
        _switcherDim?.RaiseToTop();
        var selected = _effects.SwitcherSelected;
        for (var distance = _switcherWindows.Count - 1; distance >= 0; distance--)
        {
            for (var i = 0; i < _switcherWindows.Count; i++)
            {
                if (Math.Abs(i - selected) == distance && _switcherWindows[i].EffectTree is { IsDestroyed: false } tree)
                {
                    tree.RaiseToTop();
                }
            }
        }
    }

    private void EndSwitcher(bool focus)
    {
        if (!_effects.SwitcherActive)
        {
            return;
        }

        var selected = _effects.SwitcherSelected;
        _effects.SwitcherEnd();
        _switcherDim?.Destroy();
        _switcherDim = null;
        if (focus && selected >= 0 && selected < _switcherWindows.Count)
        {
            switch (_switcherWindows[selected])
            {
                case Window window when _windows.Contains(window):
                    FocusWindow(window);
                    break;

                case XWindow xwindow when _xwindows.Contains(xwindow):
                    FocusXWindow(xwindow);
                    break;
            }
        }

        _switcherWindows.Clear();
    }

    private void DropSwitcherCard(IGrabTarget card)
    {
        if (!_effects.SwitcherActive || !_switcherWindows.Contains(card))
        {
            return;
        }

        foreach (var candidate in _switcherWindows)
        {
            if (candidate != card && SwitcherCardLive(candidate))
            {
                return;
            }
        }

        EndSwitcher(focus: false);
    }

    private static readonly double[] ScaleSteps = [1, 1.25, 1.5, 2];

    private void CycleScale()
    {
        var view = _views.FirstOrDefault(v => _layout.OutputAt(_cursorX, _cursorY) == v.Output)
            ?? _views.FirstOrDefault();
        if (view is null)
        {
            return;
        }

        var index = Array.FindIndex(ScaleSteps, s => Math.Abs(s - view.Output.Scale) < 0.001);
        SetOutputScale(view, ScaleSteps[(index + 1) % ScaleSteps.Length]);
    }

    private void SetOutputScale(OutputView view, double scale)
    {
        using var state = new OutputState();
        if (!view.Output.Commit(state.SetScale(scale)))
        {
            _log.Warn($"scale {scale} refused by {view.Output.Name}");
            return;
        }

        BasinReport.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
        Relayout();
        RefreshOutputLayout();
    }

    private void RefreshOutputColor(OutputView view)
    {
        if (_colorConfiguration is not { } colorConfiguration)
        {
            return;
        }

        var description = colorConfiguration.DescriptionOf(view.Output);
        if (Basin.Capabilities.ImageDescription.ContentComparer.Equals(description, view.ColorDescription))
        {
            return;
        }

        view.ColorDescription = description;
        view.KmsColorRouted = colorConfiguration.RouteKmsPipeline(view.Output);
        RefreshGammaBaseline(view);
        _color.SetOutputDescription(view.Global, description);
        RefreshSurfaceLuts();
        view.Scheduler?.ScheduleRepaint();
    }

    private static bool TouchesColor(in OutputConfigurationEntry entry) =>
        entry.HighDynamicRange is not null || entry.WideColorGamut is not null ||
        entry.SdrBrightnessNits is not null || entry.SdrGamutWideness is not null ||
        entry.ColorProfileSource is not null || entry.IccProfilePath is not null ||
        entry.HdrColorProfileSource is not null || entry.HdrIccProfilePath is not null ||
        entry.BrightnessOverrides is not null;

    private void OnOutputConfigurationApplied(IReadOnlyList<OutputConfigurationEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (TouchesColor(entry) && _views.FirstOrDefault(v => v.Output == entry.Output) is { } colorView)
            {
                RefreshOutputColor(colorView);
            }
        }

        foreach (var entry in entries)
        {
            if (entry.ReplicationSourceUuid is not { } uuid ||
                _views.FirstOrDefault(v => v.Output == entry.Output) is not { } replicaView)
            {
                continue;
            }

            replicaView.ReplicaSource = uuid.Length > 0
                ? _views.FirstOrDefault(v =>
                    v != replicaView && Basin.Desktop.OutputUuid.For(v.Output) == uuid)
                : null;
            replicaView.Scheduler?.ScheduleRepaint();
        }

        foreach (var entry in entries)
        {
            if (_views.FirstOrDefault(v => v.Output == entry.Output) is { } view &&
                entry is { Enabled: true, Position: not null })
            {
                view.AutoLayout = false;
            }
        }

        Relayout();
        RefreshOutputLayout();
        foreach (var entry in entries)
        {
            var box = _layout.BoxOf(entry.Output);
            BasinReport.Line($"CONFIGURED {entry.Output.Name} enabled={(entry.Enabled ? "yes" : "no")} " + $"{box.Width}x{box.Height}+{box.X}+{box.Y} scale {entry.Output.Scale}");
        }
    }

    private void RefreshOutputLayout()
    {
        ArrangeLayerSurfaces();
        ReapplyPinnedGeometry();
        UpdateSurfacePresence();
        foreach (var window in _windows)
        {
            window.RefreshFrame();
        }

        foreach (var xwindow in _xwindows)
        {
            xwindow.Layout();
        }

        if (_pointer is { } pointer)
        {
            pointer.Reposition();
        }

        foreach (var view in _views)
        {
            if (!_layout.Contains(view.Output))
            {
                continue;
            }

            if (_fullRepaint)
            {
                view.Output.RequestFrame();
            }
            else
            {
                view.Scheduler?.ScheduleRepaint();
            }
        }
    }

    private Basin.Capabilities.OutputVrrPolicy VrrPolicyOf(IOutput output) =>
        _outputConfiguration is { } configuration && configuration.TryRead(output, out var state) &&
        state.VrrPolicy is { } policy
            ? policy
            : Basin.Capabilities.OutputVrrPolicy.Automatic;

    private sealed class OutputView(OutputBase output, OutputGlobal global)
    {
        public OutputBase Output { get; } = output;

        public OutputGlobal Global { get; } = global;

        public MemoryBuffer? Target { get; set; }

        public Swapchain? Swapchain { get; set; }

        public SceneOutput? SceneOutput { get; set; }

        public OutputScheduler? Scheduler { get; set; }

        public bool AutoLayout { get; set; } = true;

        public bool FrameDonesPending { get; set; }

        public bool AutoVrrActive { get; set; }

        public OutputView? ReplicaSource { get; set; }

        public long Rendered { get; set; }

        public (ulong TimeNs, uint RefreshNs, ulong Sequence)? LastPresent { get; set; }

        public bool PresentDiscarded { get; set; }

        public IBuffer? LastPresentedBuffer { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public IAllocator? Allocator { get; set; }

        public ulong[]? SwapModifiers { get; set; }

        public bool IsSecondary { get; set; }

        public Basin.Capabilities.ImageDescription ColorDescription { get; set; } = Basin.Capabilities.ImageDescription.Srgb;

        public bool KmsColorRouted { get; set; }

        public Box UsableArea { get; set; }

        public ulong GroupId { get; set; }

        public Basin.Capabilities.WorkspaceSet<Workspace> Workspaces { get; } = new();

        public Workspace? Active
        {
            get => Workspaces.Active;
            set => Workspaces.Active = value;
        }
    }

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

    internal sealed class XWindow : IGrabTarget
    {
        private readonly TinyComp _comp;
        private Frame? _frame;
        private FrameCornerRig? _cornerRig;
        private bool _active;
        private bool _maximized;
        private RestoreGeometry _restore;

        public XWindow(TinyComp comp, Basin.XWayland.XWaylandWindow xwin, SceneSurface scene, bool framable)
        {
            _comp = comp;
            XWin = xwin;
            Framable = framable;
            Tree = scene.Tree.Parent!;
            Tree.SetPosition(xwin.X, xwin.Y);
            SceneSurface = scene;
            comp.RefreshSurfaceLuts();
            comp.ApplyCorners(SceneSurface);
            xwin.TitleChanged += Layout;
            xwin.IconChanged += RefreshIcon;
            UpdateDecorations();
            RefreshIcon();
        }

        public Frame? Frame => _frame;

        private MemoryBuffer? _iconBuffer;

        private void RefreshIcon()
        {
            _iconBuffer?.Destroy();
            _iconBuffer = null;
            if (XWin.Icon is { } icon)
            {
                var buffer = new MemoryBuffer(icon.Width, icon.Height, DrmFormat.Argb8888);
                if (buffer.BeginDataAccess(BufferDataAccess.Write, out var view))
                {
                    unsafe
                    {
                        for (var y = 0; y < icon.Height; y++)
                        {
                            var row = (uint*)(view.Data + y * view.Stride);
                            for (var x = 0; x < icon.Width; x++)
                            {
                                var argb = icon.Pixels[y * icon.Width + x];
                                var a = argb >> 24;
                                row[x] = (a << 24)
                                    | ((((argb >> 16) & 0xFF) * a / 255) << 16)
                                    | ((((argb >> 8) & 0xFF) * a / 255) << 8)
                                    | ((argb & 0xFF) * a / 255);
                            }
                        }
                    }

                    buffer.EndDataAccess();
                    _iconBuffer = buffer;
                }
                else
                {
                    buffer.Destroy();
                }
            }

            Layout();
        }

        public Basin.XWayland.XWaylandWindow XWin { get; }

        public Workspace? Workspace { get; set; }

        public SceneTree Tree { get; }

        public SceneTree? EffectTree => Tree;

        public SceneSurface SceneSurface { get; }

        public bool Framable { get; }

        public int X => XWin.X;

        public int Y => XWin.Y;

        public (int Width, int Height) GeometrySize => (XWin.Width, XWin.Height);

        public void MoveTo(int x, int y) => ResizeTo(x, y, XWin.Width, XWin.Height, ResizeEdges.None);

        public void ResizeTo(int x, int y, int width, int height, ResizeEdges edges)
        {
            XWin.Configure(x, y, width, height);
            Layout();
        }

        public void SetResizing(bool resizing)
        {
        }

        public void Layout()
        {
            Tree.SetPosition(XWin.X, XWin.Y);
            ReportGeometry();
            _comp._workspaceModel.RaiseMembersChanged();
            if (_frame is not null && XWin.Width > 0 && XWin.Height > 0)
            {
                _frame.Visible = true;
                _frame.Configure(new Box(0, 0, XWin.Width, XWin.Height), _comp.ScaleAt(X + 1, Y + 1), BuildState());
                _frame.Commit();
            }
        }

        public void ReportGeometry()
        {
            var client = new Box(XWin.X, XWin.Y, Math.Max(XWin.Width, 1), Math.Max(XWin.Height, 1));
            var frame = _frame is null ? client : FrameBox;
            _comp._xwaylandModule.Toplevels?.SetGeometry(XWin, frame, client);
        }

        public void SetNoBorderOverride(bool noBorder)
        {
            _noBorderOverride = noBorder;
            if (noBorder)
            {
                DisposeFrame();
                _comp.ApplyCorners(SceneSurface);
                ReportDecoration();
                ReportGeometry();
            }
            else
            {
                UpdateDecorations();
            }
        }

        private bool? _noBorderOverride;

        private bool WantsFrame => Framable && (_noBorderOverride is { } o ? !o : XWin.WantsDecorations);

        private void ReportDecoration() =>
            _comp._xwaylandModule.Toplevels?.SetDecoration(XWin, noBorder: !WantsFrame, userCanSet: Framable);

        internal void DisposeFrame()
        {
            _cornerRig?.Dispose();
            _cornerRig = null;
            _frame?.Dispose();
            _frame = null;
        }

        public void UpdateDecorations()
        {
            ReportDecoration();
            if (!WantsFrame)
            {
                DisposeFrame();
                _comp.ApplyCorners(SceneSurface);
                ReportGeometry();
                return;
            }

            if (_frame is not null || _comp.CreateFrameRenderer() is not { } renderer)
            {
                return;
            }

            _frame = new Frame(_comp.UIHost, renderer, Tree)
            {
                MenuLayer = _comp._layers.Overlay,
                TouchSlop = TouchGripSlop,
            };
            _frame.Requested += OnFrameAction;
            _frame.Faulted += e => _comp.Log.Error($"frame fault {XWin.Class}: {e.Message}");
            SceneSurface.Tree.RaiseToTop();
            Layout();
            if (_comp._cornerRadius > 0)
            {
                _cornerRig = new FrameCornerRig(_comp._renderer, _frame, SceneSurface.Content, _comp._cornerRadius);
            }
        }

        public void SetDecorationFocus(bool active)
        {
            _active = active;
            Layout();
        }

        public Box FrameBox
        {
            get
            {
                if (_frame is null)
                {
                    return default;
                }

                var insets = _frame.Measure(BuildState(), _comp.ScaleAt(X + 1, Y + 1));
                return new Box(
                    X - insets.Left,
                    Y - insets.Top,
                    XWin.Width + insets.Left + insets.Right,
                    XWin.Height + insets.Top + insets.Bottom);
            }
        }

        private FrameState BuildState() => new()
        {
            Title = XWin.Title,
            AppId = XWin.Class,
            Icon = new FrameIcon(null, _iconBuffer),
            Active = _active,
            Maximized = _maximized,
            Capabilities = FrameCapabilities.Maximize,
        };

        private void OnFrameAction(FrameAction action)
        {
            switch (action.Kind)
            {
                case FrameActionKind.Close:
                    XWin.Close();
                    break;
                case FrameActionKind.ToggleMaximize:
                    ToggleMaximize();
                    break;
                case FrameActionKind.Move:
                    _comp.BeginMove(this);
                    break;
                case FrameActionKind.Resize:
                    _comp.BeginResize(this, (ResizeEdges)action.Edges);
                    break;
            }
        }

        public void ToggleMaximize()
        {
            if (_maximized)
            {
                _maximized = false;
                if (_restore.TryGet(out var saved))
                {
                    _restore = RestoreGeometry.None;
                    ResizeTo(saved.X, saved.Y, saved.Width, saved.Height, ResizeEdges.None);
                }
                else
                {
                    _comp.PlaceCascade(this);
                }

                return;
            }

            var view = _comp._views.FirstOrDefault(v => _comp._layout.OutputAt(_comp._cursorX, _comp._cursorY) == v.Output)
                ?? _comp._views[0];
            _restore = _restore.Saving(new Box(XWin.X, XWin.Y, XWin.Width, XWin.Height));
            _maximized = true;
            ApplyMaximizeGeometry(view);
        }

        private void ApplyMaximizeGeometry(OutputView view)
        {
            var box = _comp._layout.BoxOf(view.Output);
            var usable = view.UsableArea.IsEmpty ? box with { X = 0, Y = 0 } : view.UsableArea;
            var insets = _frame?.Measure(BuildState(), _comp.ScaleAt(X + 1, Y + 1)) ?? default;
            ResizeTo(
                box.X + usable.X + insets.Left,
                box.Y + usable.Y + insets.Top,
                usable.Width - insets.Left - insets.Right,
                usable.Height - insets.Top - insets.Bottom,
                ResizeEdges.None);
        }

        public void ReapplyPinnedGeometry()
        {
            if (!_maximized || _comp._views.Count == 0)
            {
                return;
            }

            var view = _comp._views.FirstOrDefault(v => _comp._layout.OutputAt(X + 1, Y + 1) == v.Output)
                ?? _comp._views[0];
            ApplyMaximizeGeometry(view);
        }

        public void Destroy()
        {
            if (!SceneSurface.IsDestroyed)
            {
                SceneSurface.Destroy();
            }

            DisposeFrame();
            Tree.Destroy();
            _iconBuffer?.Destroy();
            _iconBuffer = null;
        }
    }

    internal sealed class Window : IGrabTarget
    {
        private readonly TinyComp _comp;

        public Window(TinyComp comp, XdgToplevelWindow toplevel)
        {
            _comp = comp;
            Toplevel = toplevel;
            toplevel.Xdg.Mapped += () =>
            {
                Workspace = comp.CurrentWorkspace();
                Tree = new SceneTree(Workspace?.Tree ?? comp._layers.Windows);
                Tree.SetPosition(X, Y);
                SceneSurface = new SceneSurface(Tree, toplevel.Surface);
                comp.RefreshSurfaceLuts();
                comp.ApplyBlur(SceneSurface);
                comp.ApplyCorners(SceneSurface);
                comp.OnWindowMapped(this);
                comp._effects.CancelClosing(toplevel.Surface);
                SetDecorated(comp.IsServerDecorated(toplevel));
                ReportGeometry();
                comp._effects.OnMapped(Tree);
            };
            toplevel.Xdg.Unmapped += () =>
            {
                comp._effects.OnClosing(toplevel.Surface, Tree, comp._layers.Top, _cornerRig);
                _cornerRig = null;
                SceneSurface?.Destroy();
                SceneSurface = null;
                _frame?.Dispose();
                _frame = null;
                Tree?.Destroy();
                Tree = null;
                comp.OnWindowGone(this);
            };
            toplevel.Xdg.Committed += ApplyResizeAnchor;
            toplevel.Xdg.Committed += LayoutDecorations;
            toplevel.Xdg.Committed += ReportGeometry;
            toplevel.TitleChanged += RefreshFrame;
            toplevel.AppIdChanged += RefreshFrame;
            toplevel.MinimizeRequested += () => comp.SetMinimized(this, true);
            toplevel.MoveRequested += serial => comp.BeginMove(this, serial);
            toplevel.ResizeRequested += (serial, edges) => comp.BeginResize(this, edges, serial);
            toplevel.MaximizeRequested += maximized =>
            {
                toplevel.RequestConfigure();
                if (maximized != toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized))
                {
                    ApplyMaximize(maximized);
                }
            };
            toplevel.FullscreenRequested += fullscreen =>
            {
                toplevel.RequestConfigure();
                if (fullscreen == toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen))
                {
                    return;
                }

                toplevel.SetFullscreen(fullscreen);
                if (fullscreen)
                {
                    var output = comp._layout.OutputAt(comp._cursorX, comp._cursorY) ?? comp._views[0].Output;
                    var box = comp._layout.BoxOf(output);
                    _restore = (X, Y);
                    MoveTo(box.X, box.Y);
                    toplevel.SetSize(box.Width, box.Height);
                }
                else
                {
                    MoveTo(_restore.X, _restore.Y);
                    toplevel.SetSize(0, 0);
                }
            };
        }

        public XdgToplevelWindow Toplevel { get; }

        public Workspace? Workspace { get; set; }

        public bool Minimized { get; set; }

        public SceneTree? Tree { get; private set; }

        public SceneTree? EffectTree => Tree;

        public SceneSurface? SceneSurface { get; private set; }

        public int X { get; private set; }

        public int Y { get; private set; }

        private (int X, int Y) _restore;

        public void MoveTo(int x, int y)
        {
            X = x;
            Y = y;
            Tree?.SetPosition(x, y);
            ReportGeometry();
            _comp._workspaceModel.RaiseMembersChanged();

            if (_frame is not null && Tree is not null && _comp.ScaleForWindow(this) != _frameScale)
            {
                LayoutDecorations();
            }
        }

        private double _frameScale = 1.0;

        public (int Width, int Height) GeometrySize
        {
            get
            {
                var geometry = Toplevel.Xdg.EffectiveGeometry;
                return (geometry.Width, geometry.Height);
            }
        }

        public void ResizeTo(int x, int y, int width, int height, ResizeEdges edges)
        {
            _resizeAnchor = ResizeAnchor.For(edges, x, y, width, height);
            if (_resizeAnchor is null)
            {
                MoveTo(x, y);
            }

            Toplevel.SetSize(width, height);
            ReportGeometry();
            _comp._workspaceModel.RaiseMembersChanged();

            if (_frame is not null && width > 0 && height > 0 &&
                !Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen))
            {
                _pendingFrameSize = (width, height);
                ScheduleFrameConfigure();
            }
        }

        private (int Width, int Height)? _pendingFrameSize;
        private bool _frameConfigureScheduled;

        private void ScheduleFrameConfigure()
        {
            if (_frameConfigureScheduled)
            {
                return;
            }

            _frameConfigureScheduled = true;
            _comp.Loop.AddIdle(() =>
            {
                _frameConfigureScheduled = false;
                if (_frame is not { } frame || _pendingFrameSize is not { } size)
                {
                    return;
                }

                _pendingFrameSize = null;
                var g = Toplevel.Xdg.EffectiveGeometry;
                frame.Configure(new Box(g.X, g.Y, size.Width, size.Height), _comp.ScaleAt(X + 1, Y + 1), BuildState());
            });
        }

        public void SetResizing(bool resizing)
        {
            _resizing = resizing;
            Toplevel.SetResizing(resizing);
        }

        private bool _resizing;

        private ResizeAnchor? _resizeAnchor;

        private void ApplyResizeAnchor()
        {
            if (_resizeAnchor is not { } anchor)
            {
                return;
            }

            var (width, height) = GeometrySize;
            var (x, y) = anchor.PositionFor(width, height, X, Y);
            if (x != X || y != Y)
            {
                MoveTo(x, y);
            }

            _resizeAnchor = ResizeAnchor.AfterCommit(_resizeAnchor, _resizing);
        }

        private RestoreGeometry _maximizeRestore;

        public void ToggleMaximize() =>
            ApplyMaximize(!Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized));

        private void ApplyMaximize(bool maximized)
        {
            Toplevel.SetMaximized(maximized);
            if (!maximized)
            {
                if (_maximizeRestore.TryGet(out var saved))
                {
                    _maximizeRestore = RestoreGeometry.None;
                    MoveTo(saved.X, saved.Y);
                    Toplevel.SetSize(saved.Width, saved.Height);
                }
                else
                {
                    Toplevel.SetSize(0, 0);
                    _comp.PlaceCascade(this);
                }

                return;
            }

            var comp = _comp;
            var view = comp._views.FirstOrDefault(v => comp._layout.OutputAt(comp._cursorX, comp._cursorY) == v.Output)
                ?? comp._views[0];
            var geometry = Toplevel.Xdg.EffectiveGeometry;
            _maximizeRestore = _maximizeRestore.Saving(new Box(X, Y, geometry.Width, geometry.Height));
            ApplyMaximizeGeometry(view);
        }

        private void ApplyMaximizeGeometry(OutputView view)
        {
            var comp = _comp;
            var box = comp._layout.BoxOf(view.Output);
            var usable = view.UsableArea.IsEmpty ? box with { X = 0, Y = 0 } : view.UsableArea;
            var insets = _frame?.Measure(BuildState(), comp.ScaleAt(X + 1, Y + 1)) ?? default;
            MoveTo(box.X + usable.X + insets.Left, box.Y + usable.Y + insets.Top);
            Toplevel.SetSize(usable.Width - insets.Left - insets.Right, usable.Height - insets.Top - insets.Bottom);
        }

        public void ReapplyPinnedGeometry()
        {
            var comp = _comp;
            if (comp._views.Count == 0)
            {
                return;
            }

            var view = comp._views.FirstOrDefault(v => comp._layout.OutputAt(X + 1, Y + 1) == v.Output)
                ?? comp._views[0];
            if (Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen))
            {
                var box = comp._layout.BoxOf(view.Output);
                MoveTo(box.X, box.Y);
                Toplevel.SetSize(box.Width, box.Height);
            }
            else if (Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized))
            {
                ApplyMaximizeGeometry(view);
            }
        }

        private Frame? _frame;
        private FrameCornerRig? _cornerRig;
        private bool _active;
        private string? _iconName;

        public Frame? Frame => _frame;

        public void SetDecorated(bool decorated)
        {
            _comp._xdgToplevels.SetDecoration(Toplevel, noBorder: !decorated, userCanSet: true);
            if (!decorated)
            {
                _cornerRig?.Dispose();
                _cornerRig = null;
                _frame?.Dispose();
                _frame = null;
                if (SceneSurface is not null)
                {
                    _comp.ApplyCorners(SceneSurface);
                }

                ReportGeometry();
                return;
            }

            if (Tree is null || _frame is not null || _comp.CreateFrameRenderer() is not { } renderer)
            {
                return;
            }

            _frame = new Frame(_comp.UIHost, renderer, Tree)
            {
                MenuLayer = _comp._layers.Overlay,
                TouchSlop = TouchGripSlop,
            };
            _frame.Requested += OnFrameAction;
            _frame.Faulted += e => _comp.Log.Error($"frame fault {Toplevel.AppId}: {e.Message}");
            SceneSurface?.Tree.RaiseToTop();
            LayoutDecorations();
            ReportGeometry();
            if (_comp._cornerRadius > 0 && SceneSurface is not null)
            {
                _cornerRig = new FrameCornerRig(_comp._renderer, _frame, SceneSurface.Content, _comp._cornerRadius);
            }
        }

        public void SetDecorationFocus(bool active)
        {
            _active = active;
            RefreshFrame();
        }

        public void SetIconName(string? name)
        {
            _iconName = name;
            RefreshFrame();
        }

        public Box FrameBox
        {
            get
            {
                if (_frame is null)
                {
                    return default;
                }

                var g = Toplevel.Xdg.EffectiveGeometry;
                var insets = _frame.Measure(BuildState(), _comp.ScaleAt(X + 1, Y + 1));
                return new Box(
                    X + g.X - insets.Left,
                    Y + g.Y - insets.Top,
                    g.Width + insets.Left + insets.Right,
                    g.Height + insets.Top + insets.Bottom);
            }
        }

        public void ReportGeometry()
        {
            if (Tree is null)
            {
                return;
            }

            var g = Toplevel.Xdg.EffectiveGeometry;
            var client = new Box(X + g.X, Y + g.Y, Math.Max(g.Width, 1), Math.Max(g.Height, 1));
            var frame = _frame is null ? client : FrameBox;
            _comp._xdgToplevels.SetGeometry(Toplevel, frame, client);
        }

        private void LayoutDecorations()
        {
            if (_frame is null)
            {
                return;
            }

            var geometry = Toplevel.Xdg.EffectiveGeometry;
            var visible = geometry.Width > 0 && geometry.Height > 0 &&
                !Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen);
            _frame.Visible = visible;
            if (!visible)
            {
                return;
            }

            var scale = _comp.ScaleForWindow(this);
            _frameScale = scale;
            if (!_frame.HasPendingFor(geometry, scale))
            {
                _frame.Configure(geometry, scale, BuildState());
            }

            _frame.Commit();
        }

        internal Box ScaleBox
        {
            get
            {
                var g = Toplevel.Xdg.EffectiveGeometry;
                return new Box(X + g.X, Y + g.Y, Math.Max(g.Width, 1), Math.Max(g.Height, 1));
            }
        }

        public void RefreshFrame()
        {
            if (_frame is null || Tree is null)
            {
                return;
            }

            LayoutDecorations();
        }

        private FrameState BuildState() => new()
        {
            Title = Toplevel.Title,
            AppId = Toplevel.AppId,
            Icon = new FrameIcon(_iconName, null),
            Active = _active,
            Maximized = Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Maximized),
            Fullscreen = Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen),
            Resizing = Toplevel.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Resizing),
            Capabilities = FrameCapabilities.Maximize,
        };

        private void OnFrameAction(FrameAction action)
        {
            switch (action.Kind)
            {
                case FrameActionKind.Close:
                    Toplevel.Close();
                    break;
                case FrameActionKind.ToggleMaximize:
                    ToggleMaximize();
                    break;
                case FrameActionKind.Minimize:
                    _comp.SetMinimized(this, true);
                    break;
                case FrameActionKind.Move:
                    _comp.BeginMove(this);
                    break;
                case FrameActionKind.Resize:
                    _comp.BeginResize(this, (ResizeEdges)action.Edges);
                    break;
            }
        }

        public bool Owns(Surface surface)
        {
            for (var candidate = surface; candidate is not null;)
            {
                if (candidate == Toplevel.Surface)
                {
                    return true;
                }

                candidate = candidate.SubsurfaceRole?.Parent;
            }

            return false;
        }
    }
}
