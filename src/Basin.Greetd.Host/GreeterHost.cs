using Avalonia;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.UI.Avalonia;

namespace Basin.Greetd.Host;

public sealed partial class GreeterHost : IDisposable
{
    private const uint KeyS = 31;

    private readonly GreeterHostOptions _options;
    private readonly BasinLogger _log;
    private readonly IRenderer _renderer;
    private readonly string _rendererName;
    private readonly IAllocator? _deviceAllocator;
    private readonly BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Basin.Scene.Scene _scene = new();
    private readonly SceneTree _backgroundLayer;
    private readonly SceneTree _boxLayer;
    private readonly SceneTree _popupLayer;
    private readonly BasinServices _services;
    private readonly Basin.Seat.Seat _seatState;
    private readonly OutputDriver _outputs;
    private readonly CompositorRunLoop _loop;
    private readonly BasinGlGpu? _gpu;
    private readonly AvaloniaUIHost _ui;
    private readonly UIDriver _uiDriver;
    private readonly OutputScreens _screens;
    private readonly UISurfaceIndex _index = new();
    private readonly CursorController _cursor;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private readonly ProcessTracker _processes;
    private readonly ActiveOutputTracker _active;
    private readonly OutputUISurface _box;
    private readonly Dictionary<IOutput, OutputUISurface> _backgrounds = [];
    private readonly string? _greetdPath;
    private ChromeSeat? _seat;
    private ScreenReader? _reader;
    private double _textScale = 1.0;
    private string _exit = "signal";
    private int _status;

    private GreeterHost(GreeterHostOptions options, BasinLogger log, Func<BasinPlatformOptions, AvaloniaUIHost> start)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _log = log;
        _greetdPath = GreetdPath(options);

        var name = options.Renderer;
        var stack = RendererCatalog.CreateWithFallback(ref name, RendererCatalog.FindRenderNode(), fallback => log.Warn($"{fallback.Describe()}"));
        _rendererName = name;
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;

        _host = BasinHost.Create(HostOptions.ForBackend(options.Backend) with
        {
            SocketFd = options.SocketFd,
            Handoff = options.Handoff.Resolve(options.Settings.Handoff.Mode),
            Greeter = true,
            Greetd = _greetdPath is not null,
        });
        _host.HandoffRetained += retention => BasinReport.Line(HandoffLines.Retain(retention));

        _services = _host.CreateServices().Use(_layout);
        _services.Install(new ProtocolPack([new CompositorModule(), new Basin.Seat.SeatModule(), new ShmModule(), new SubcompositorModule(), new Basin.Shell.Xdg.XdgShellModule()]));
        _services.Freeze();
        _processes = new ProcessTracker(_host.Loop, ReaderEnvironment);
        _host.Display.SetGlobalFilter((client, _, _) => client.TryGetCredentials(out var credentials) && _processes.OwnsTree(credentials.Pid));
        _seatState = _services.Require<Basin.Seat.Seat>();
        _seatState.Keyboard.SetKeymap(Basin.Seat.SystemKeymap.Read());

        _backgroundLayer = new SceneTree(_scene.Root);
        _boxLayer = new SceneTree(_scene.Root);
        _popupLayer = new SceneTree(_scene.Root);

        if (_host.Session is { } session && options.IsDrm)
        {
            _input = new Basin.Backend.Libinput.LibinputBackend(_host.Loop, session);
        }

        _active = new ActiveOutputTracker(_layout);
        _active.Changed += output =>
        {
            BasinReport.Line($"BOX {output.Name}");
            PlaceBox();
            BoxMoved?.Invoke(output);
        };
        _cursor = new CursorController(_layout);
        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Frames = _services.Require<Basin.Capabilities.IFrameClock>(),
            Requested = options.Outputs,
            Scales = [.. options.Scales],
            ContinuousRepaint = options.Frames > 0,
            NestedName = index => $"{options.Program}-{index + 1}",
            HeadlessMode = options.HeadlessMode,
        };
        _outputs.ConfigureHandoff(options.Settings.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, options.Settings.Handoff.DurationMs, options.Settings.Handoff.HoldLimitMs);
        _loop = new CompositorRunLoop(_host, _outputs) { OutroBudget = TimeSpan.FromMilliseconds(750) };
        _outputs.Emptied += () => Stop("outputs");
        _outputs.HandoffInherited += inheritance => BasinReport.Line(HandoffLines.Inherit(inheritance));
        _outputs.ModesetRefused += card => log.Error($"modeset refused by {card.Name} in every mode");
        _outputs.Added += view => BasinReport.Line($"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        _outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene);
        _outputs.Removed += view => _cursor.RemoveOutput(view.Output);
        _outputs.Added += OnOutputAdded;
        _outputs.Removed += OnOutputRemoved;
        _outputs.LayoutChanged += PlaceAll;
        _outputs.BeforeRepaint += _ => _uiDriver?.SyncPopups();

        _screens = new OutputScreens(_outputs, _layout);
        _gpu = _renderer.Device is { } device
            ? BasinGlGpu.TryCreate(device.DevicePath, device as Basin.Render.Gl.GlDevice, _renderer.DmabufTextureFormats)
            : null;
        _ui = start(new BasinPlatformOptions
        {
            EventLoop = _host.Loop,
            Screens = _screens,
            Theme = options.Theme,
            Gpu = _gpu,
            Configure = options.Configure,
        });
        _uiDriver = new UIDriver(_ui, _host.Loop) { PopupLayer = _popupLayer, Index = _index };
        _box = new OutputUISurface(_boxLayer, _ui, _index) { PreciseDamage = true };
        _box.Realized += surface =>
        {
            BoxRealized?.Invoke(surface);
            _seat?.Router.SetKeyboardFocus(surface);
        };
    }

    public static GreeterHost Create<TApp>(GreeterHostOptions options, BasinLogger log)
        where TApp : Application, new() => new(options, log, static platform => BasinPlatform.Start<TApp>(platform));

    public GreeterHostOptions Options => _options;

    public BasinHost Host => _host;

    public BasinServices Services => _services;

    public Basin.Seat.Seat SeatState => _seatState;

    public OutputDriver Outputs => _outputs;

    public OutputLayout Layout => _layout;

    public Basin.Scene.Scene Scene => _scene;

    public IRenderer Renderer => _renderer;

    public string RendererName => _rendererName;

    public AvaloniaUIHost Ui => _ui;

    public UIDriver UiDriver => _uiDriver;

    public UISurfaceIndex Index => _index;

    public SceneTree PopupLayer => _popupLayer;

    public OutputUISurface Box => _box;

    public ChromeSeat Seat => _seat ?? throw new InvalidOperationException("the greeter host has not started");

    public ProcessTracker Processes => _processes;

    public ActiveOutputTracker Active => _active;

    public ScreenReader? Reader => _reader;

    public GreeterMemory Memory => _options.Memory;

    public double TextScale
    {
        get => _textScale;
        set
        {
            _textScale = value;
            PlaceBox();
        }
    }

    public Action<double, double>? PointerMoved { get; set; }

    public Action<double, double>? Touched { get; set; }

    public Action? Pressed { get; set; }

    public Func<uint, uint, bool, bool>? KeyHook { get; set; }

    public event Action<IOutput, OutputUISurface>? BackgroundAdded;

    public event Action<IOutput>? BackgroundRemoved;

    public event Action<Basin.Capabilities.IUISurface>? BoxRealized;

    public event Action<IOutput>? BoxMoved;

    public event Action? BoxPlaced;

    public SceneTree AddLayer()
    {
        var layer = new SceneTree(_scene.Root);
        _popupLayer.RaiseToTop();
        return layer;
    }

    public void Start()
    {
        _outputs.CreateInitialOutputs();
        if (_options.IsDrm)
        {
            _outputs.AdoptSecondaryCards();
        }

        if (_host.Parent is not null)
        {
            _cursor.UseParentCursor();
        }

        _cursor.Load(new ShmAllocator(), 64, 64, 24);
        _seat = new ChromeSeat(_host, _seatState, _layout, _scene, _index, _cursor, _input, _log)
        {
            PointerMoved = (x, y) =>
            {
                _active.Moved(x, y);
                PointerMoved?.Invoke(x, y);
            },
            Touched = (x, y) =>
            {
                _active.Moved(x, y);
                Touched?.Invoke(x, y);
            },
            Pressed = () => Pressed?.Invoke(),
            TouchFramed = () => _outputs.ScheduleAll(),
            KeyHook = (time, key, pressed) => (_options.ReaderChord && ReaderChord(key, pressed)) || KeyHook?.Invoke(time, key, pressed) == true,
        };
        _seat.Binder.VtSwitching = true;
        if (_box.Surface is { } realized)
        {
            _seat.Router.SetKeyboardFocus(realized);
        }

        if (_options.IsDrm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("no connected output");
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += () => Stop("parent");
        }

        _reader = new ScreenReader(_processes, _options.Settings.Orca, _options.Settings.ScreenReader, _log);
        _reader.Started += process => BasinReport.Line($"READER on pid={process.Pid}");
        _reader.StartFailed += _ => BasinReport.Line("READER failed");
        _reader.Stopped += () => BasinReport.Line("READER off");
    }

    public void StartAccessibility() => _ = StartAtSpiAsync();

    public void Stop(string reason, int status = 0)
    {
        if (_exit == "signal")
        {
            _exit = reason;
            _status = status;
            _loop.Stopping = reason == "start" ? HandoffStop.SessionStart : status != 0 ? HandoffStop.Failure : HandoffStop.Orderly;
        }

        _loop.Stop();
    }

    public void WriteScreenshot(string? path)
    {
        if (path is not { Length: > 0 } || _outputs.Views.FirstOrDefault() is not { } view)
        {
            return;
        }

        _uiDriver.Pump();
        if (SceneScreenshot.Write(_scene, _renderer, view.Output, path))
        {
            _log.Info($"screenshot written to {path}");
        }
    }

    public int Run(Action? beforeCommands = null)
    {
        var hangup = _host.Loop.AddSignal(Signal.Hangup, _ => Reload?.Invoke());
        BasinReport.Line($"RENDERER {_rendererName}");
        BasinReport.Line(string.IsNullOrEmpty(_host.Socket) ? "SOCKET (inherited)" : $"SOCKET {_host.Socket}");
        if (_outputs.Views.FirstOrDefault() is { } first)
        {
            var box = _layout.BoxOf(first.Output);
            Seat.Warp(box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
        }

        _uiDriver.Woken += _outputs.ScheduleAll;
        _uiDriver.Start();
        beforeCommands?.Invoke();
        WireCommands();
        _flow?.ShowUsers();

        _loop.Iterating += _uiDriver.Pump;
        _loop.Frames = _options.Frames;
        _loop.Run();
        _loop.Iterating -= _uiDriver.Pump;
        _uiDriver.Woken -= _outputs.ScheduleAll;
        hangup.Remove();

        BasinReport.Line($"EXIT {_exit}");
        return _status;
    }

    public void PlaceBox()
    {
        if (_active.Current is not { } output || _outputs.Views.All(view => !ReferenceEquals(view.Output, output)))
        {
            return;
        }

        _box.Zoom = _textScale;
        _box.Place(_layout.BoxOf(output), output.Scale);
        BoxPlaced?.Invoke();
        _outputs.ScheduleAll();
    }

    private void OnOutputAdded(OutputView view)
    {
        var surface = new OutputUISurface(_backgroundLayer, _ui, _index) { PreciseDamage = true, InputEnabled = false };
        _backgrounds[view.Output] = surface;
        BackgroundAdded?.Invoke(view.Output, surface);
        _active.Added(view.Output);
        PlaceAll();
    }

    private void OnOutputRemoved(OutputView view)
    {
        if (_backgrounds.Remove(view.Output, out var surface))
        {
            surface.Dispose();
            BackgroundRemoved?.Invoke(view.Output);
        }

        _active.Removed(view.Output, _seat?.PointerX ?? 0, _seat?.PointerY ?? 0);
        PlaceAll();
    }

    private void PlaceAll()
    {
        foreach (var view in _outputs.Views)
        {
            if (_backgrounds.TryGetValue(view.Output, out var background))
            {
                background.Place(_layout.BoxOf(view.Output), view.Output.Scale);
            }
        }

        PlaceBox();
    }

    private IReadOnlyDictionary<string, string?> ReaderEnvironment() => new Dictionary<string, string?>
    {
        ["WAYLAND_DISPLAY"] = _host.Socket,
        ["DISPLAY"] = null,
        ["GDK_BACKEND"] = "wayland",
    };

    private async Task StartAtSpiAsync()
    {
        var reason = await _ui.StartAccessibilityAsync();
        if (reason is null)
        {
            BasinReport.Line("A11Y on");
            _reader?.AccessibilityStarted(true);
            return;
        }

        _log.Info($"no accessibility tree: {reason}");
        BasinReport.Line("A11Y off");
        _reader?.AccessibilityStarted(false);
    }

    private bool ReaderChord(uint key, bool pressed)
    {
        if (key != KeyS || _reader is not { IsAvailable: true } reader || _seatState.Keyboard.Keymap is not { } keymap)
        {
            return false;
        }

        var (depressed, _, _, _) = _seatState.Keyboard.ModifierState;
        if (keymap.GetModIndex("Mod4") is not { } super || keymap.GetModIndex("Mod1") is not { } alt ||
            (depressed & (1u << (int)super)) == 0 || (depressed & (1u << (int)alt)) == 0)
        {
            return false;
        }

        if (pressed)
        {
            reader.Toggle();
        }

        return true;
    }

    public void Dispose()
    {
        _host.DisconnectClients();
        _ipc?.Dispose();
        _settle?.Dispose();
        _reader?.Dispose();
        _processes.TerminateAll(TimeSpan.FromMilliseconds(500));
        _processes.Dispose();
        _flow?.Dispose();
        _client?.Dispose();
        _demo?.Dispose();
        _seat?.Dispose();
        _box.Dispose();
        foreach (var surface in _backgrounds.Values)
        {
            surface.Dispose();
        }

        _backgrounds.Clear();
        _uiDriver.Dispose();
        _screens.Dispose();
        _cursor.Dispose();
        _input?.Dispose();
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
