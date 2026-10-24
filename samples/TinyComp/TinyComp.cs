using System.Diagnostics;
using Basin;
using Basin.Host;
using Basin.Backend.Libinput;
using Basin.Cli;
using Basin.Effects;
using Basin.Backend.Wayland;
using Basin.Scene;
using Basin.Hypr;
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
    internal WaylandEventLoop Loop => _loop;
    private readonly WaylandBackend? _backend;
    private NestedSeam? _seam;
    private WaylandSeamTextInput? _seamTextInput;
    private readonly List<SurfaceBox> _caretSurfaces = [];
    private readonly Basin.Session.ISession? _session;
    private readonly Basin.Backend.Drm.DrmBackend? _drm;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private readonly Basin.Seat.Backends.InputConfigurator _inputConfigurator = new();
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
    private SceneTree _wallpapers = null!;
    private SceneTree _backdrop = null!;
    private readonly EffectsPolicy _effects;
    private PostEffects _post = null!;
    private ScreenShader _shader = null!;
    private FeedbackEffects? _feedback;
    private bool _dimOverview;
    private Basin.Effects.DropShadowTexture? _shadowTexture;
    private Basin.Desktop.SessionLockManager _sessionLock = null!;
    private Basin.Desktop.SessionLockSceneDriver _lockDriver = null!;
    private readonly XdgDecorationManager _decorations;
    private readonly XdgToplevelDragManager _toplevelDrags;
    private readonly Basin.Desktop.KdeServerDecorationManager _kdeDecorations;
    private readonly Basin.Desktop.DecorationPreferences _decorationPreferences;
    private Basin.Capabilities.IUIHost? _uiHost;
    private Basin.Capabilities.IUIHost? _quillHost;
    private string? _quillDeclined;
    private readonly string? _renderNode;
    private QuillDemo? _quillDemo;
    private FrameHosts? _frameHosts;
    private bool _quillBlurClamped;
    private bool _metacityBlurClamped;
    private bool _metacityOpaqueWarned;
    private bool _msstylesQuillWarned;
    private bool _msstylesKept;
    private bool _glassWarned;
    private bool _glassTried;
    private Basin.Effects.IBackdropGlass? _glassEffect;
    private Basin.Effects.DropShadowTexture? _glassShadowTexture;
    private Basin.Effects.DropShadowTexture? _glassGlowTexture;
    private Basin.Frames.Quill.QuillFrameTheme? _quillTheme;
    private FrameStyle _frameStyle;
    private FrameTheme? _frameTheme;
    private Basin.UI.Skia.FlatFrameTheme? _flatTheme;
    private MetacityFrames? _metacity;
    private MsStylesFrames? _msstyles;

    internal IFrameRenderer? CreateFrameRenderer() => CreateFrameRenderer(_frameStyle);

    internal IFrameRenderer? CreateFrameRenderer(FrameStyle style) => style switch
    {
        FrameStyle.Beos => new BeosFrameRenderer(FrameThemeOrLoad()),
        FrameStyle.Flat => new Basin.UI.Skia.FlatFrameRenderer(FlatThemeOrLoad()),
        FrameStyle.Metacity => (_metacity ??= MetacityFrames.Load(_config, FrameThemeOrLoad())).CreateRenderer(),
        FrameStyle.MsStyles => CreateMsStylesFrameRenderer(),
        FrameStyle.Quill => CreateQuillFrameRenderer(),
        FrameStyle.Glass => CreateGlassFrameRenderer(),
        _ => null,
    };

    private IFrameRenderer CreateGlassFrameRenderer()
    {
        if (QuillHost() is null)
        {
            if (!_glassWarned)
            {
                _glassWarned = true;
                _log.Warn($"[frame] style = \"glass\" is unavailable on the {_rendererName} renderer, flat frames used: {_quillDeclined}");
            }

            return new Basin.UI.Skia.FlatFrameRenderer(FlatThemeOrLoad());
        }

        if (GlassEffect() is null)
        {
            if (!_glassWarned)
            {
                _glassWarned = true;
                _log.Warn($"[frame] style = \"glass\" needs backdrop effects, the {_rendererName} renderer has none, plain quill frames used");
            }

            return CreateQuillFrameRenderer();
        }

        return new Basin.Frames.Quill.QuillFrameRenderer(GlassThemeOf(_config));
    }

    internal static int GlassContentRadiusOf(Config config) =>
        (int)Math.Round(GlassThemeOf(config).ResolvedContentRadius);

    private static Basin.Frames.Quill.QuillFrameTheme GlassThemeOf(Config config)
    {
        var theme = config.GlassPalette == "light"
            ? Basin.Frames.Quill.QuillFrameTheme.LightGlass()
            : Basin.Frames.Quill.QuillFrameTheme.DarkGlass();
        theme.CornerRadius = (float)Math.Max(0, config.GlassCornerRadius);
        theme.FontSize = (float)Math.Max(1, config.GlassFontSize);
        theme.Bevel = (float)Math.Max(0, config.GlassBevel);
        theme.AnimationMs = (int)Math.Max(1, config.GlassAnimationMs);
        theme.Border = (int)Math.Round(Math.Max(0, config.GlassInset));
        theme.ContentRadius = config.GlassContentRadius < 0 ? -1f : (float)config.GlassContentRadius;
        return theme;
    }

    internal static Basin.Effects.GlassOptions GlassOptionsOf(Config config)
    {
        var tint = config.GlassTint ?? (config.GlassPalette == "light" ? 0x60F0F3F8u : 0x60202832u);
        var inactiveAlpha = (uint)Math.Round((tint >> 24) * 0.6);
        var border = config.GlassContentBorder;
        var inactiveBorderAlpha = (uint)Math.Round((border >> 24) * 0.6);
        return new Basin.Effects.GlassOptions
        {
            Strength = Math.Clamp((int)Math.Round(config.GlassBlur), 1, Basin.Effects.BlurStrength.Steps),
            Kernel = config.GlassBlurKernel switch
            {
                "vista" => Basin.Effects.BlurKernel.VistaDwm,
                "smol" => Basin.Effects.BlurKernel.SmolGaussian,
                _ => Basin.Effects.BlurKernel.DualKawase,
            },
            Pass = config.GlassPass == "fused" ? Basin.Effects.GlassPass.Fused : Basin.Effects.GlassPass.Composite,
            Tint = tint,
            InactiveTint = (tint & 0x00FFFFFFu) | (inactiveAlpha << 24),
            Saturation = config.GlassSaturation,
            Noise = config.GlassNoise,
            Lens = config.GlassRefraction,
            Specular = config.GlassSpecular,
            LightAngle = config.GlassLightAngle,
            Glint = config.GlassGlint,
            GlintRadius = config.GlassGlintRadius,
            ContentBorder = border,
            InactiveContentBorder = (border & 0x00FFFFFFu) | (inactiveBorderAlpha << 24),
            ContentBorderWidth = Math.Max(0, config.GlassContentBorderWidth),
        };
    }

    private Basin.Effects.IBackdropGlass? GlassEffect()
    {
        if (_glassTried)
        {
            return _glassEffect;
        }

        _glassTried = true;
        _glassEffect = Basin.Effects.BackdropGlass.For(_renderer);
        if (_glassEffect is { } glass)
        {
            glass.Options = GlassOptionsOf(_config);
            var asked = (int)Math.Round(_config.GlassBlur);
            if (asked != glass.Options.Strength)
            {
                _log.Warn($"[frame.glass] blur {asked} reads as strength {glass.Options.Strength}, the range is 1 to {Basin.Effects.BlurStrength.Steps}");
            }
        }

        return _glassEffect;
    }

    private IFrameRenderer CreateQuillFrameRenderer()
    {
        if (QuillHost() is null)
        {
            _log.Warn($"[frame] style = \"quill\" is unavailable on the {_rendererName} renderer, flat frames used: {_quillDeclined}");
            return new Basin.UI.Skia.FlatFrameRenderer(FlatThemeOrLoad());
        }

        return new Basin.Frames.Quill.QuillFrameRenderer(QuillThemeOrLoad())
        {
            LightTheme = QuillThemeOf(_config, "light"),
            DarkTheme = QuillThemeOf(_config, "dark"),
        };
    }

    private IFrameRenderer CreateMsStylesFrameRenderer()
    {
        var frames = _msstyles ??= MsStylesFrames.Load(_config, MsStylesIcon);
        var quill = frames.Painter == "quill" && QuillHost() is not null;
        if (frames.Painter == "quill" && !quill && !_msstylesQuillWarned)
        {
            _msstylesQuillWarned = true;
            _log.Warn($"[frame.msstyles] painter = \"quill\" is unavailable on the {_rendererName} renderer, Skia paints the frames: {_quillDeclined}");
        }

        var renderer = frames.CreateRenderer(quill);
        IFrameRenderer menu = quill
            ? new Basin.Frames.Quill.QuillFrameRenderer(QuillThemeOrLoad())
            : new Basin.UI.Skia.FlatFrameRenderer(FlatThemeOrLoad());
        return new MenuDonorFrameRenderer(renderer, menu);
    }

    private SkiaSharp.SKImage? MsStylesIcon(string name, int scale) => FrameThemeOrLoad().IconFor(name, scale);

    internal Basin.Capabilities.IFrameClock FrameClock => _frameClock;

    private void ApplyBlurKernel(Config config)
    {
        if (_blurEffect is not { } blur)
        {
            return;
        }

        var kernel = config.BlurKernel switch
        {
            "vista" => Basin.Effects.BlurKernel.VistaDwm,
            "smol" => Basin.Effects.BlurKernel.SmolGaussian,
            _ => Basin.Effects.BlurKernel.DualKawase,
        };
        if (blur.Options.Kernel != kernel)
        {
            blur.Options = blur.Options with { Kernel = kernel };
        }
    }

    private Basin.Frames.Quill.QuillFrameTheme QuillThemeOrLoad() => _quillTheme ??= QuillThemeOf(_config);

    private static Basin.Frames.Quill.QuillFrameTheme QuillThemeOf(Config config) => QuillThemeOf(config, config.QuillPalette);

    private static Basin.Frames.Quill.QuillFrameTheme QuillThemeOf(Config config, string palette)
    {
        var theme = palette == "light"
            ? Basin.Frames.Quill.QuillFrameTheme.Light()
            : new Basin.Frames.Quill.QuillFrameTheme();
        theme.CornerRadius = (float)config.QuillCornerRadius;
        theme.FontSize = (float)config.QuillFontSize;
        theme.Frosted = config.QuillBackdropBlur > 0;
        theme.FrostAlpha = (byte)Math.Clamp(Math.Round(config.QuillFrostOpacity * 255), 0, 255);
        return theme;
    }

    private Basin.Capabilities.IUIHost? QuillHost()
    {
        if (_quillHost is not null)
        {
            return _quillHost;
        }

        if (_quillDeclined is not null)
        {
            return null;
        }

        var host = Basin.Renderers.QuillHosts.For(_renderer, _allocator, _renderNode, out _quillDeclined, out var vulkanDeclined);
        if (vulkanDeclined is not null)
        {
            _log.Warn($"quill: frames cannot use the {_rendererName} renderer's VulkanDevice, trying a GlDevice: {vulkanDeclined}");
        }

        switch (host)
        {
            case null:
                return null;
            case Basin.UI.Quill.QuillVulkanUIHost:
                _log.Info($"quill: frames share the {_rendererName} renderer's VulkanDevice");
                break;
            case Basin.UI.Quill.QuillUIHost { OwnsDevice: true } glHost:
                _log.Info($"quill: frames draw on their own GlDevice on {glHost.Device.DevicePath} beside the {_rendererName} renderer");
                break;
            default:
                _log.Info($"quill: frames share the {_rendererName} renderer's GlDevice");
                break;
        }

        _quillHost = host;
        FrameHosts.Add(_quillHost);
        return _quillHost;
    }

    internal void ApplyFrameBackdrop(Frame frame, IFrameRenderer renderer)
    {
        if (renderer is Basin.Frames.Quill.QuillFrameRenderer { Theme.Glass: true } && GlassEffect() is { } glass)
        {
            frame.UnderlayKey = frame;
            frame.UnderlayCoversClient = _config.GlassClientGlass;
            frame.UnderlayEffect = glass;
            return;
        }

        if (_blurEffect is not { } blur)
        {
            return;
        }

        int strength;
        if (renderer is Basin.Frames.Quill.QuillFrameRenderer { Theme.Frosted: true })
        {
            var asked = (int)Math.Round(_config.QuillBackdropBlur);
            strength = Math.Clamp(asked, 1, Basin.Effects.BlurStrength.Steps);
            if (asked != strength && !_quillBlurClamped)
            {
                _quillBlurClamped = true;
                _log.Warn($"[frame.quill] backdrop_blur {asked} reads as strength {strength}, the range is 1 to {Basin.Effects.BlurStrength.Steps}");
            }
        }
        else if (renderer is Basin.Frames.Metacity.MetacityFrameRenderer { Frosted: true } metacity)
        {
            var asked = (int)Math.Round(_config.MetacityBackdropBlur);
            strength = Math.Clamp(asked, 1, Basin.Effects.BlurStrength.Steps);
            if (asked != strength && !_metacityBlurClamped)
            {
                _metacityBlurClamped = true;
                _log.Warn($"[frame.metacity] backdrop_blur {asked} reads as strength {strength}, the range is 1 to {Basin.Effects.BlurStrength.Steps}");
            }

            if (!_metacityOpaqueWarned && !metacity.Painter.Theme.HasTranslucentBackground)
            {
                _metacityOpaqueWarned = true;
                _log.Warn($"[frame.metacity] backdrop_blur is set, but the {metacity.Painter.Theme.Name} theme sets no window_background_alpha, so its opaque frames hide the blur");
            }
        }
        else if (renderer is MenuDonorFrameRenderer { Frame: Basin.Frames.MsStyles.MsStylesFrameRenderer or Basin.Frames.MsStyles.Quill.MsStylesQuillFrameRenderer }
                 && _msstyles is { Frosted: true, Glass: true })
        {
            frame.BackdropKey = frame;
            blur.SetSurface(frame, new BlurSurfaceOptions { Kernel = Basin.Effects.BlurKernel.VistaDwm });
            frame.BackdropEffect = blur;
            return;
        }
        else
        {
            return;
        }

        frame.BackdropKey = frame;
        blur.SetSurface(frame, new BlurSurfaceOptions { Strength = strength, Kernel = Basin.Effects.BlurKernel.DualKawase });
        frame.BackdropEffect = blur;
    }

    internal void ForgetFrameBackdrop(Frame? frame)
    {
        if (frame is not null)
        {
            _blurEffect?.ForgetSurface(frame);
            _glassEffect?.ForgetSurface(frame);
        }
    }

    private FrameTheme FrameThemeOrLoad() => _frameTheme ??= new FrameTheme((float)_config.FontSize);

    private Basin.UI.Skia.FlatFrameTheme FlatThemeOrLoad() => _flatTheme ??= FlatThemeOf(FrameThemeOrLoad());

    private static Basin.UI.Skia.FlatFrameTheme FlatThemeOf(FrameTheme theme) =>
        new(theme.Typeface, theme.FontSize) { Icons = theme.IconFor };

    internal Basin.Capabilities.IUIHost UIHost => _uiHost ??= SkiaUIHosts.For(_renderer);

    internal FrameHosts FrameHosts => _frameHosts ??= new FrameHosts(UIHost);
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
    private Basin.Desktop.PointerConstraintDriver _constraints = null!;
    private Basin.Desktop.ForeignToplevelListManager _toplevelList = null!;
    private readonly WorkspacePolicy _workspaceModel;
    private Basin.Desktop.PointerGesturesManager _gestures = null!;
    private Basin.Desktop.KeyboardShortcutsInhibitManager _shortcutsInhibit = null!;
    private Basin.Desktop.TextInputManager _textInput = null!;
    private Basin.Desktop.ColorManager _color = null!;
    private Basin.XWayland.XwaylandShellGlobal _xwaylandShell = null!;
    private Basin.XWayland.XWaylandDisplay? _xwayland;
    private Basin.Config.LaunchRuleDensityControl? _densityControl;
    private Basin.Freedesktop.DesktopEntries? _desktopEntries;
    private readonly ProcessTracker _processes;
    private readonly List<XWindow> _xwindows = [];
    private readonly Basin.XWayland.XWaylandSceneDriver _xSceneDriver = new();
    private Basin.Desktop.ContentTypeManager _contentType = null!;
    private IOutputConfiguration? _outputConfiguration;
    private Basin.Color.ColorOutputConfiguration? _colorConfiguration;
    private Basin.Desktop.ColorCapabilityPack _colorPack = null!;
    private Basin.Desktop.OrientationSensor? _orientationSensor;
    private Basin.Capabilities.Defaults.LayoutOutputConfiguration? _layoutConfiguration;
    private Basin.Desktop.FifoManager _fifo = null!;
    private Basin.Capabilities.IFrameClock _frameClock = null!;
    private Basin.Desktop.IdleManager _idle = null!;
    private Basin.Desktop.LayerShellSceneDriver _layerDriver = null!;
    private readonly Basin.Desktop.PopupPlacer _popupPlacer;
    private readonly OutputLayout _layout = new();
    private readonly List<HostChromeLease> _hostChrome = [];
    private bool _probed;
    private bool _outputsCreated;
    private readonly List<Basin.Render.Vulkan.VulkanDeviceBlitter> _blitters = [];

    private IBackdropBlur? _blurEffect;
    private IPixelShader? _fireShader;
    private int _cornerRadius;
    private CrossDeviceImportCache? _blitCache;
    private readonly List<Window> _windows = [];
    private readonly string _socket;
    private Basin.Seat.Backends.DragIconFollower _dragIcon = null!;
    private Window? _focused;
    private XWindow? _focusedX;
    private DragMode _mode;
    private IGrabTarget? _grabWindow;
    private double _grabX, _grabY;
    private double _grabStartX, _grabStartY;
    private Box _grabStart;
    private ResizeEdges _grabEdges;
    private double _cursorX, _cursorY;
    private double _lastRawX, _lastRawY;
    private readonly Basin.Host.CompositorRunLoop _runLoop;
    private readonly long _frames;
    private readonly bool _fullRepaint;
    private bool _offload;
    private readonly bool _hdr;
    private Basin.Color.PanelTable _panels = Basin.Color.PanelTable.Empty;
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

    private readonly string? _init;

    public bool ExitWithCommand { get; set; }

    public bool Subreaper { get; set; }

    public int? ReadyFd
    {
        get => _host.ReadyFd;
        set => _host.ReadyFd = value;
    }

    private int _exitStatus;

    private Basin.Host.InitProcess? _initProcess;
    private Basin.Transport.Waypipe.WaypipeListener? _channels;
    private Basin.Transport.Waypipe.WaypipeAdoption? _adoption;
    private Wayland.Server.WlServerDisplay.GlobalFilter? _privilegedFilter;
    private Config _config;
    private readonly string? _configPath;
    private string _rendererName = "vulkan";
    private bool _fireShaderTried;

    private void ApplyEffectShaders()
    {
        var fire = _effects.CloseKind == "fire-gpu"
            || _config.Rules.Any(static rule => rule.Close == "fire-gpu");
        _effects.FireShaderHandle = fire ? FireShaderHandle() : null;
    }

    private void ApplyCornerRadius(int radius) => _cornerRadius = Math.Max(0, radius);

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
            _log.Warn($"effects: {_rendererName} has no pixel shaders, fire-gpu uses the particle burn");
        }

        return _fireShader;
    }

    public TinyComp(Config config, BackendKind backend = BackendKind.Nested, int socketFd = -1, BasinLogger log = default, bool managedTransport = false, string? channelEndpoint = null, string? configPath = null, Basin.Cli.IpcChoice? ipc = null, string? init = null, HandoffChoice handoff = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var drm = backend == BackendKind.Drm;
        _config = config;
        _configPath = configPath;
        _init = init;
        _shortcuts = new Basin.Hypr.PreferredTriggerShortcuts();
        ConfigureShortcuts(config);
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
        _panels = Basin.Color.PanelTable.Load(_log);
        _colorSource = config.ColorSource;
        _damageTint = config.DamageTint;
        _scales = config.Scales;
        _ = LoadStepTextures(config, reload: false);
        _host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(drm ? "drm" : backend == BackendKind.Headless ? "headless" : "nested") with
            {
                Transport = managedTransport || channelEndpoint is not null
                    ? Basin.Host.HostTransport.Managed
                    : Basin.Host.HostTransport.LibWayland,
                SocketFd = socketFd,
                Handoff = handoff.Resolve(config.Handoff.Mode),
            });
        _processes = new ProcessTracker(_host.Loop, () => ProcessTracker.SessionEnvironment(_host.Socket, _xwayland?.DisplayName, _ipc?.Path, _xwaylandModule?.ChildEnvironment()))
        {
            X11Routes = density => _xwaylandModule?.Route(density),
            X11Default = launch => _config.Launch.ForArgv(launch.DesktopId, launch.Argv),
        };
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
        _wallpapers = new SceneTree(_layers.Background);
        _backdrop = new SceneTree(_layers.Background);
        _pointerRefresh = new PointerRefresh(_scene, _loop, RefreshPointer);

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
                        _report.Line($"BLIT {node} ({device.Driver})");
                    }
                    catch (Exception e) when (e is InvalidOperationException or DllNotFoundException)
                    {
                        _report.Line($"BLIT {node} unavailable: {e.Message}");
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

            _renderNode = _drm.RenderNodePath;
            var stack = CreateStack(ref rendererName, _drm.RenderNodePath);
            _renderer = stack.Renderer;

            _allocator = stack.DeviceAllocator;
        }
        else
        {
            _backend = _host.Parent;

            _renderNode = Basin.Renderers.RendererCatalog.FindRenderNode();
            var stack = CreateStack(ref rendererName, _renderNode);
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
            VrrPacing = config.VrrPacing,
            AllowTearing = config.AllowTearing,
            NestedName = _ => null,
        };
        _driver.ConfigureHandoff(config.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, config.Handoff.DurationMs, config.Handoff.HoldLimitMs);
        _runLoop.Outputs = _driver;
        _driver.HandoffLayer = _layers.Handoff;
        _runLoop.Outro = new Basin.Host.WindowsFadeOutro(_layers, TimeSpan.FromMilliseconds(300));
        WireOutputDriver();

        _popupPlacer = new Basin.Desktop.PopupPlacer(_layout);
        _rendererName = rendererName;
        _effects.SlideEnabled = rendererName != "pixman";

        var servicePack = new Basin.Desktop.DesktopServicePack(_scene, _layout, _renderer, _drm);
        var capturePack = servicePack.Capture;
        capturePack.Capture.Allocator = _allocator;
        _capture = capturePack.Capture;
        _dmabufCapture = capturePack.DmabufCapture;
        _captureIndex = capturePack.Index;
        _stack = capturePack.Stack;
        _driver.Capture = capturePack;
        _gamma = servicePack.Drm.Gamma;
        _cursorTheme = servicePack.CursorTheme;
        _orientationSensor = new Basin.Desktop.OrientationSensor(_loop);
        _colorPack = new Basin.Desktop.ColorCapabilityPack(_layout, _renderer);
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
        _colorConfiguration.DescriptionChanged += output =>
        {
            if (Views.FirstOrDefault(v => v.Output == output) is { } colorView)
            {
                RefreshOutputColor(colorView);
                colorView.Scheduler?.ScheduleRepaint();
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

        if (BackdropBlurs.For(_renderer) is { } blur)
        {
            _blurEffect = blur;
            ApplyBlurKernel(config);
            _services.Use<Basin.Capabilities.IBackgroundEffects>(blur);
        }

        _post = new PostEffects(_renderer, rendererName, _log);
        _post.Configure(config);
        _shader = new ScreenShader(_renderer, rendererName, _allocator, _log);
        _shader.Configure(config);
        ApplyEffectShaders();
        ApplyCornerRadius(config.CornerRadius);

        var xwayland = new Basin.XWayland.XWaylandModule { IncludeLowDensity = config.XWaylandLowDensity };
        _xwaylandModule = xwayland;
        var desktopPack = Basin.Desktop.DesktopPack.For("tinycomp");
        var hyprPack = HyprPackFor(config);
        if (hyprPack is not null)
        {
            _hyprCtm = new HyprCtm(this);
            _services.Use<Basin.Capabilities.ICtmControl>(_hyprCtm);
        }

        _services.Use<Basin.Capabilities.IGlobalShortcuts>(_shortcuts);

        LinuxDmabufModule? dmabufModule = null;
        if (_renderer.Device is { } dmabufDevice)
        {
            dmabufModule = new LinuxDmabufModule(
                _renderer.DmabufTextureFormats,
                dmabufDevice.DevicePath,
                extraTranches: _blitters.Select(b => (b.DevicePath, b.ImportableFormats)).ToArray(),
                captureFormats: _renderer.DmabufRenderFormats);
        }

        _services
            .Install(desktopPack)
            .Install(xwayland);
        if (hyprPack is not null)
        {
            _services.Install(hyprPack);
        }

        if (dmabufModule is not null)
        {
            _services.Install(dmabufModule);
        }

        _services.UseDefault<IInputDeviceConfiguration>(_inputConfigurator);
        CreateLayerDrivers();
        _densityControl = new Basin.Config.LaunchRuleDensityControl(
            () => _services.Find<Basin.Capabilities.IToplevelModel>(), _processes, _host.Loop, () => SettingsPath, _log)
        {
            DesktopIdForAppId = appId => (_desktopEntries ??= new Basin.Freedesktop.DesktopEntries()).FindForAppId(appId)?.Id,
        };
        _densityControl.RulesWritten += rules => _config.Launch = rules;
        _services.Use<Basin.Capabilities.IX11DensityControl>(_densityControl);
        _services.Freeze();
        _dmabufGlobal = dmabufModule?.Global;
        WireInputCapture();

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
            var window = FindWindow(surface);
            SceneNode? content = window?.Tree;
            SceneNode? client = window?.SceneSurface?.Tree;
            if (content is null)
            {
                foreach (var xwindow in _xwindows)
                {
                    if (xwindow.XWin.Surface == surface)
                    {
                        content = xwindow.Tree;
                        client = xwindow.SceneSurface.Tree;
                        break;
                    }
                }
            }

            return content is null ? null : new ToplevelCaptureTrees(content, null, client);
        });
        _xdgToplevels.ActivateRequested += toplevel =>
        {
            if (FindWindow(toplevel) is { } window)
            {
                if (IsShelved(window))
                {
                    Unshelve(window, null, null);
                    return;
                }

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
        _services.Require<XdgDialogManager>().ModalChanged += (toplevel, modal) =>
            FindWindow(toplevel)?.SetModal(modal);
        _xdgToplevels.CaptureExclusionRequested += (toplevel, excluded) =>
            _xdgToplevels.SetExcludedFromCapture(toplevel, excluded);
        _xdgToplevels.MoveRequested += (toplevel, box) => FindWindow(toplevel)?.MoveTo(box.X, box.Y);
        _xdgToplevels.ResizeRequested += (toplevel, box) =>
        {
            if (FindWindow(toplevel) is { } window)
            {
                window.ResizeTo(window.X, window.Y, box.Width, box.Height, ResizeEdges.None);
            }
        };

        WireLayerShell();
        _toplevelDrags = _services.Require<XdgToplevelDragManager>();
        _decorations = _services.Require<XdgDecorationManager>();
        _sessionLock = _services.Require<Basin.Desktop.SessionLockManager>();
        WireSessionLock();
        _screencopy = _services.Require<Basin.Desktop.ScreencopyManager>();
        _relativePointer = _services.Require<Basin.Desktop.RelativePointerManager>();
        _constraints = _services.Require<Basin.Desktop.PointerConstraintDriver>();
        _constraints.Allow = constraint => !IsOnDeformedWindow(constraint.Surface) &&
            (constraint.Kind == Basin.Desktop.ConstraintKind.Lock || ScaleOfSurface(constraint.Surface) == 1.0);
        _constraints.Changed += (_, _) => SyncParentPointerLock();
        _toplevelList = _services.Require<Basin.Desktop.ForeignToplevelListManager>();
        _gestures = _services.Require<Basin.Desktop.PointerGesturesManager>();
        _shortcutsInhibit = _services.Require<Basin.Desktop.KeyboardShortcutsInhibitManager>();
        _services.Require<Basin.Desktop.VirtualKeyboardManager>().KeymapSubmitted +=
            (fd, _) => fd.Close();
        _services.Require<Basin.Desktop.SystemBellManager>().Rang += _ => _report.Line($"BELL");
        var transientSeats = _services.Require<Basin.Desktop.TransientSeatManager>();
        transientSeats.SeatRequested += request =>
            request.Create(seat => new Basin.Desktop.SceneSeatInput(seat, _scene, _layout));
        transientSeats.SeatCreated += seat => _report.Line($"SEAT {seat.Name}");
        _kdeDecorations = _services.Require<Basin.Desktop.KdeServerDecorationManager>();
        if (_blurEffect is not null)
        {
            _scene.SurfaceBackdrop = new Basin.Desktop.BackgroundBlurDriver(
                _services.Require<Basin.Desktop.BackgroundEffectManager>(), _blurEffect);
        }
        _decorationPreferences = new Basin.Desktop.DecorationPreferences(_decorations, _kdeDecorations, _xdgToplevels);
        _decorationPreferences.Changed += (surface, serverSide) => FindWindow(surface)?.SetDecorated(serverSide);
        _services.Require<XdgToplevelIconManager>().IconChanged +=
            (toplevel, name) => FindWindow(toplevel)?.SetIconName(name);
        if (!_colorPack.Attach(_services, _scene))
        {
            throw new InvalidOperationException("No color manager is registered. The color pack needs one to attach.");
        }

        _color = _colorPack.Manager;
        _colorPack.Surfaces!.DeclinesInverseToneMapping = surface => FindWindow(surface)?.Quirks.InverseToneMapping == false;
        _compositor.SurfaceCreated += surface => surface.Destroyed += UpdateEdrDemand;
        _colorPack.Surfaces.CountChanged += attached => _report.Line(CompositorLines.Luts(attached));
        foreach (var view in Views)
        {
            _colorPack.AddOutput(view.Global, view.Output, view.Color);
            SyncBlendSpace(view);
        }

        _color.SurfaceDescriptionChanged += (_, _) => UpdateEdrDemand();
        if (_hdr)
        {
            _hdrDemand = Basin.Desktop.HdrDemand.Watch(_toplevels, _color, _loop);
            _hdrDemand.Changed += OnHdrDemandChanged;
            _hdrShown = _hdrDemand.Demand;
        }

        _contentType = _services.Require<Basin.Desktop.ContentTypeManager>();
        _fifo = _services.Require<Basin.Desktop.FifoManager>();
        _frameClock = _services.Require<Basin.Capabilities.IFrameClock>();
        _driver.Frames = _frameClock;
        _xwaylandShell = _services.Require<Basin.XWayland.XwaylandShellGlobal>();
        _xwayland = xwayland.Native!;
        ApplyXWaylandScale();
        _xSceneDriver.Letterbox = true;
        _xSceneDriver.ShapeRenderer = _renderer;
        _xSceneDriver.ShapeAllocator = _allocator;
        _xSceneDriver.ManagedParent = ManagedXParent;
        _xSceneDriver.OverrideRedirectParent = AdoptOverrideRedirect;
        _xSceneDriver.Adopted += OnXAdopted;
        _xSceneDriver.Removed += OnXRemoved;
        _xSceneDriver.ActivationRequested += ActivateXWindow;
        foreach (var display in xwayland.Displays)
        {
            display.WindowManagerReady += wm =>
            {
                _xSceneDriver.Attach(wm);
                if (display.Toplevels is { } xToplevels)
                {
                    xToplevels.NoBorderRequested += (xwin, noBorder) =>
                        FindXWindow(xwin)?.SetNoBorderOverride(noBorder);
                    xToplevels.CaptureExclusionRequested += (xwin, excluded) =>
                        xToplevels.SetExcludedFromCapture(xwin, excluded);
                    xToplevels.MoveRequested += (xwin, box) => FindXWindow(xwin)?.MoveTo(box.X, box.Y);
                    xToplevels.ResizeRequested += (xwin, box) =>
                    {
                        if (FindXWindow(xwin) is { } xwindow)
                        {
                            xwindow.ResizeTo(xwindow.X, xwindow.Y, box.Width, box.Height, ResizeEdges.None);
                        }
                    };
                }

                _report.Line($"XWAYLAND WM {display.DisplayName}");
            };
        }

        _report.Line($"XWAYLAND {_xwayland.DisplayName}");
        if (xwayland.LowDensity is { } lowDensity)
        {
            _report.Line($"XWAYLAND LOW {lowDensity.DisplayName}");
        }
        _privilegedFilter = (client, _, interfaceName) =>
            !Basin.Desktop.PrivilegedProtocols.Contains(interfaceName) || IsTrusted(client);
        _display.SetGlobalFilter(_privilegedFilter);
        _textInput = _services.Require<Basin.Desktop.TextInputManager>();
        _lockDriver.TextInput = _textInput;
        _fractionalScale = _services.Require<Basin.Desktop.FractionalScaleManager>();
        _presenceTracker = new SurfacePresenceTracker(_layout, AnnounceSurfaceScale);
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
        ApplyCursorIdle();
        _cursorShapes = _services.Require<Basin.Desktop.CursorShapeManager>();
        _cursorShapes.CursorRequested += _cursor.ShowImage;
        _cursor.Shapes = _cursorShapes;
        _cursor.ColorProfiles = _services.Find<Basin.Capabilities.IColorProfileService>();
        _color.OutputDescriptionChanged += (global, description) => _cursor.Describe(global.Output, description);
        _seat.Pointer.MapToSurface = _scene.TryMapToSurface;
        _seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;

        if (drm)
        {
            _pointer = new LayoutPointer(_layout);
            _driver.CreateInitialOutputs();

            _driver.AdoptSecondaryCards();

            _outputsCreated = true;
            SetupTouch();
            WireLibinput(_input!);
            _touchBinder!.RelativePointer = _relativePointer;
            _touchBinder!.Constraints = _constraints;
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
            _seat.Pointer.CursorRequested += OnClientCursor;
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
        _report.Line($"KEYMAP {keymapNames.Layout ?? "xkb default"}{(keymapNames.Model is { } model ? $" {model}" : string.Empty)}");
        if (_config.FrameStyle == FrameStyle.MsStyles)
        {
            _msstyles = MsStylesFrames.Load(_config, MsStylesIcon);
            _report.Line(_msstyles.Describe());
        }

        AttachInputConfigurator();

        _compositor.SurfaceCreated += surface => surface.Committed += () =>
        {
            if (surface.Current.FrameCallbacks.Count > 0)
            {
                foreach (var view in Views)
                {
                    view.Scheduler?.ScheduleRepaint();
                }
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
        WireIpc(ipc ?? Basin.Cli.IpcChoice.Off, drm ? "drm" : backend == BackendKind.Headless ? "headless" : "nested", rendererName);
        _dragIcon = new Basin.Seat.Backends.DragIconFollower(
            _seat, () => _layers.Overlay, () => (_cursorX, _cursorY))
        {
            Touch = _touchDriver?.Router,
        };
        _dragIcon.Created += _ => RefreshSurfaceLuts();
        ApplyEffectSettings(config);
        ApplyPhysicsSettings();
    }

    private void StartQuillDemo()
    {
        if (QuillHost() is not { } host)
        {
            _log.Warn($"quill demo: unavailable, {_quillDeclined}");
            return;
        }

        var scale = Views.Count > 0 ? Views[0].Output.Scale : 1.0;
        _quillDemo = new QuillDemo(_layers.Overlay, host, _loop, scale, _log);
    }

    public long Rendered => _driver.PrimaryRendered;

    private IReadOnlyList<Basin.Host.OutputView> Views => _driver.Views;

    public int Run()
    {
        _report.Line(CompositorLines.Socket(_socket));
        StartIpc();
        Basin.Cli.CurrentDesktop.Export("basin", _host.Drm is null ? null : _socket);

        if (_channelEndpoint is { } endpoint)
        {
            _report.Line($"CHANNEL {endpoint}");
            _channels = Basin.Transport.Waypipe.WaypipeListener.Bind(_loop, Basin.Transport.Waypipe.WaypipeEndpoint.ForListening(endpoint));
            var adoption = new Basin.Transport.Waypipe.WaypipeAdoption(_display, _services.Require<ClientBufferRegistry>(), _compositor, _dmabufGlobal is not null, _privilegedFilter);
            _adoption = adoption;
            var listener = _channels;
            listener.ChannelAccepted += accepted =>
            {
                var index = listener.Accepted;
                accepted.Ended += failure =>
                {
                    if (failure is null)
                    {
                        _log.Info($"waypipe: channel {index} ended");
                    }
                    else
                    {
                        _log.Warn(failure, $"waypipe: channel {index} ended");
                    }
                };
                adoption.Adopt(accepted);
                _log.Info($"waypipe: channel {index} attached, replayed as one client");
            };
            listener.Failed += failure => _log.Error(failure, $"waypipe: channel listener failed");
            _log.Info($"waypipe: accepting channels on {listener.LocalEndPoint}");
        }

        if (_config.QuillDemo)
        {
            StartQuillDemo();
        }

        if (_config.ImGuiDemo)
        {
            StartImGuiDemo();
        }

        StartInspector();

        if (_init is { } init)
        {
            _initProcess = Basin.Host.InitProcess.Start(
                init, _socket, _xwayland?.DisplayName, _log, _xwaylandModule?.ChildEnvironment(),
                _config.Launch.ForCommand(null, init) is { } density ? _xwaylandModule?.Route(density) : null,
                Subreaper);
            if (_initProcess is { } started && started.WatchExit(_loop))
            {
                started.Exited += status =>
                {
                    _report.Line($"COMMAND exited status={status}");
                    if (ExitWithCommand)
                    {
                        _exitStatus = status;
                        _runLoop.Stop();
                    }
                };
            }
        }

        var hangup = _loop.AddSignal(Signal.Hangup, _ => Reload());
        _runLoop.Frames = _frames;
        _runLoop.Run();
        hangup.Remove();
        return _exitStatus;
    }

    public void Dispose()
    {
        _host.DisconnectClients();
        _capture.Dispose();
        _initProcess?.Stop(_log);
        _initProcess = null;
        _pointerRefresh?.Dispose();
        _hotCornerTimer?.Remove();
        _hotCornerTimer = null;
        _processes.TerminateAll(TimeSpan.FromMilliseconds(500));
        _processes.Dispose();
        _ipc?.Dispose();
        _settle?.Dispose();
        _ipc = null;
        _adoption?.Dispose();
        _adoption = null;
        _channels?.Dispose();
        _channels = null;

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
        ClearPhysics();
        _quillDemo?.Dispose();
        _quillDemo = null;
        DisposeImGui();
        DisposeSettings();
        _quillHost?.Dispose();
        _quillHost = null;
        _uiHost?.Dispose();

        _colorPack.Luts.Dispose();
        _driver.Dispose();

        _hdrDemand?.Dispose();
        _hdrDemand = null;
        _cursorIdle?.Dispose();
        _cursorIdle = null;
        _cursor.Dispose();

        foreach (var display in _xwaylandModule.Displays)
        {
            display.Dispose();
        }
        _protocolTrace?.Dispose();
        _tablets?.Dispose();
        _inputConfigurator.Dispose();
        _input?.Dispose();
        _layoutConfiguration?.Dispose();
        _orientationSensor?.Dispose();
        _blitCache?.Dispose();
        foreach (var blitter in _blitters)
        {
            blitter.Dispose();
        }

        _feedback?.Dispose();
        _feedback = null;
        _effects.Dispose();
        _post.Dispose();
        _shader.Dispose();
        _shadowTexture?.Dispose();
        _scene.SurfaceBackdrop = null;
        _blurEffect?.Dispose();
        _glassEffect?.Dispose();
        _glassShadowTexture?.Dispose();
        _glassGlowTexture?.Dispose();
        foreach (var shader in _glowShaders)
        {
            shader.Dispose();
        }

        _glowShaders.Clear();
        _fireShader?.Dispose();
        _allocator?.Dispose();
        _seam?.Dispose();
        _seam = null;
        _seamTextInput?.Dispose();
        _seamTextInput = null;
        _xSceneDriver.Dispose();
        _scene.Root.Destroy();
        _services.Dispose();
        _host.Dispose();
        _metacity?.Dispose();
        _msstyles?.Dispose();
        DisposeStepTextures();
        _flatTheme?.Dispose();
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

        bool Maximized { get; }

        (int Width, int Height)? RestoreForDrag();
    }

}
