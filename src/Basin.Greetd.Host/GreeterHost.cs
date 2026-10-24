using Avalonia;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.UI.Avalonia;

namespace Basin.Greetd.Host;

/// <summary>
/// The compositor that a greeter runs in: renderer, outputs, seat, UI host, greetd connection, flow, screen reader and control socket.
/// The program supplies the content and the hooks. Every call must come from the loop thread.
/// </summary>
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
    private BasinGlGpu? _gpu;
    private readonly Basin.Capabilities.IUIHost _ui;
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

    private GreeterHost(GreeterHostOptions options, BasinLogger log, Func<GreeterHost, Basin.Capabilities.IUIHost> start)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _log = log;
        _greetdPath = GreetdPath(options);

        var name = options.Renderer;
        var stack = RendererCatalog.CreateWithFallback(ref name, RendererCatalog.FindRenderNode(), fallback => fallback.Log(log));
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
        _outputs.ModesetRefused += card => log.Error($"drm: {card.Name} refused a modeset in every mode");
        _outputs.Added += view => BasinReport.Line($"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        _outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene);
        _outputs.Removed += view => _cursor.RemoveOutput(view.Output);
        _outputs.Added += OnOutputAdded;
        _outputs.Removed += OnOutputRemoved;
        _outputs.LayoutChanged += PlaceAll;
        _outputs.BeforeRepaint += _ => _uiDriver?.SyncPopups();

        _screens = new OutputScreens(_outputs, _layout);
        _ui = start(this);
        _uiDriver = new UIDriver(_ui, _host.Loop) { PopupLayer = _popupLayer, Index = _index };
        _box = new OutputUISurface(_boxLayer, _ui, _index) { PreciseDamage = true };
        _box.Realized += surface =>
        {
            BoxRealized?.Invoke(surface);
            _seat?.Router.SetKeyboardFocus(surface);
        };
    }

    /// <summary>
    /// Creates a host that runs the Avalonia platform with <typeparamref name="TApp"/>, on the GPU when the renderer has a device.
    /// </summary>
    /// <typeparam name="TApp">The Avalonia application type.</typeparam>
    /// <param name="options">The host options.</param>
    /// <param name="log">The logger for the host.</param>
    /// <returns>A host with no outputs yet. The caller owns it.</returns>
    /// <exception cref="InvalidOperationException">The greetd socket is not known, or a demo is asked for under greetd.</exception>
    public static GreeterHost Create<TApp>(GreeterHostOptions options, BasinLogger log)
        where TApp : Application, new() => new(options, log, static host =>
        {
            host._gpu = host._renderer.Device is { } device
                ? BasinGlGpu.TryCreate(device.DevicePath, device as Basin.Render.Gl.GlDevice, host._renderer.DmabufTextureFormats)
                : null;
            return BasinPlatform.Start<TApp>(new BasinPlatformOptions
            {
                EventLoop = host._host.Loop,
                Screens = host._screens,
                Theme = host._options.Theme,
                Gpu = host._gpu,
                Configure = host._options.Configure,
            });
        });

    /// <summary>
    /// Creates a host around a UI host that is not Avalonia, for example a Skia host.
    /// </summary>
    /// <param name="options">The host options.</param>
    /// <param name="log">The logger for the host.</param>
    /// <param name="ui">Makes the UI host for the renderer that the host chose. The host owns the result.</param>
    /// <returns>A host with no outputs yet. The caller owns it.</returns>
    /// <exception cref="InvalidOperationException">The greetd socket is not known, or a demo is asked for under greetd.</exception>
    public static GreeterHost Create(GreeterHostOptions options, BasinLogger log, Func<IRenderer, Basin.Capabilities.IUIHost> ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        return new(options, log, host => ui(host._renderer));
    }

    /// <summary>
    /// Gets the options that created the host.
    /// </summary>
    public GreeterHostOptions Options => _options;

    /// <summary>
    /// Gets the compositor bootstrap: display, loop, backend and session.
    /// </summary>
    public BasinHost Host => _host;

    /// <summary>
    /// Gets the frozen services, with the modules that the screen reader needs.
    /// </summary>
    public BasinServices Services => _services;

    /// <summary>
    /// Gets the protocol seat, which holds the keyboard and its keymap.
    /// </summary>
    public Basin.Seat.Seat SeatState => _seatState;

    /// <summary>
    /// Gets the output driver.
    /// </summary>
    public OutputDriver Outputs => _outputs;

    /// <summary>
    /// Gets the output layout.
    /// </summary>
    public OutputLayout Layout => _layout;

    /// <summary>
    /// Gets the scene, with the background, box and popup layers in that order.
    /// </summary>
    public Basin.Scene.Scene Scene => _scene;

    /// <summary>
    /// Gets the renderer that the host chose, after any fallback.
    /// </summary>
    public IRenderer Renderer => _renderer;

    /// <summary>
    /// Gets the name of the renderer that the host chose, after any fallback.
    /// </summary>
    public string RendererName => _rendererName;

    /// <summary>
    /// Gets the Avalonia UI host.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host does not run Avalonia. Use <see cref="UIHost"/> then.</exception>
    public AvaloniaUIHost Ui => _ui as AvaloniaUIHost ?? throw new InvalidOperationException("This greeter host does not run Avalonia. Use UIHost instead.");

    /// <summary>
    /// Gets the UI host for any toolkit.
    /// </summary>
    public Basin.Capabilities.IUIHost UIHost => _ui;

    /// <summary>
    /// Gets the driver that pumps the UI host on the loop.
    /// </summary>
    public UIDriver UiDriver => _uiDriver;

    /// <summary>
    /// Gets the index of the UI surfaces in the scene.
    /// </summary>
    public UISurfaceIndex Index => _index;

    /// <summary>
    /// Gets the scene layer for popups. It stays above every layer that <see cref="AddLayer"/> adds.
    /// </summary>
    public SceneTree PopupLayer => _popupLayer;

    /// <summary>
    /// Gets the login box surface. It is on the active output, with <see cref="TextScale"/> as its zoom.
    /// </summary>
    public OutputUISurface Box => _box;

    /// <summary>
    /// Gets the seat.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="Start"/> has not run.</exception>
    public ChromeSeat Seat => _seat ?? throw new InvalidOperationException("The greeter host has not started.");

    /// <summary>
    /// Gets the tracker of child processes. Only their clients see the Wayland globals.
    /// </summary>
    public ProcessTracker Processes => _processes;

    /// <summary>
    /// Gets the tracker of the output that holds the login box.
    /// </summary>
    public ActiveOutputTracker Active => _active;

    /// <summary>
    /// Gets the screen reader, or <see langword="null"/> before <see cref="Start"/>.
    /// </summary>
    public ScreenReader? Reader => _reader;

    /// <summary>
    /// Gets the greeter memory from the options.
    /// </summary>
    public GreeterMemory Memory => _options.Memory;

    /// <summary>
    /// Gets or sets the zoom of the login box. A change places the box again.
    /// </summary>
    public double TextScale
    {
        get => _textScale;
        set
        {
            _textScale = value;
            PlaceBox();
        }
    }

    /// <summary>
    /// Gets or sets a hook that receives each pointer position in layout coordinates.
    /// </summary>
    public Action<double, double>? PointerMoved { get; set; }

    /// <summary>
    /// Gets or sets a hook that receives the layout position of each touch down.
    /// </summary>
    public Action<double, double>? Touched { get; set; }

    /// <summary>
    /// Gets or sets a hook that runs on each button or key press.
    /// </summary>
    public Action? Pressed { get; set; }

    /// <summary>
    /// Gets or sets a hook that sees each key after the screen reader chord. Its arguments are time, evdev key code and pressed. A return of <see langword="true"/> takes the key.
    /// </summary>
    public Func<uint, uint, bool, bool>? KeyHook { get; set; }

    /// <summary>
    /// Occurs when an output appears, with the background surface that the host made for it. The program sets the content.
    /// </summary>
    public event Action<IOutput, OutputUISurface>? BackgroundAdded;

    /// <summary>
    /// Occurs when an output goes away, after the host disposes its background surface.
    /// </summary>
    public event Action<IOutput>? BackgroundRemoved;

    /// <summary>
    /// Occurs when the login box surface is realized, before it gets the keyboard focus.
    /// </summary>
    public event Action<Basin.Capabilities.IUISurface>? BoxRealized;

    /// <summary>
    /// Occurs when the login box moves to another output.
    /// </summary>
    public event Action<IOutput>? BoxMoved;

    /// <summary>
    /// Occurs each time the login box is placed on its output.
    /// </summary>
    public event Action? BoxPlaced;

    /// <summary>
    /// Adds a scene layer above the login box and keeps the popup layer on top, for example for a status bar or a menu.
    /// </summary>
    /// <returns>The new layer.</returns>
    public SceneTree AddLayer()
    {
        var layer = new SceneTree(_scene.Root);
        _popupLayer.RaiseToTop();
        return layer;
    }

    /// <summary>
    /// Creates the outputs, loads the cursor, and makes the seat and the screen reader.
    /// </summary>
    /// <exception cref="InvalidOperationException">The backend is <c>drm</c> and no output is connected.</exception>
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
            KeyHook = (time, key, pressed) => (_options.ReaderChord && ReaderChord(key, pressed)) || AccessChord(key, pressed) || KeyHook?.Invoke(time, key, pressed) == true,
        };
        _seat.Binder.VtSwitching = true;
        if (_box.Surface is { } realized)
        {
            _seat.Router.SetKeyboardFocus(realized);
        }

        if (_options.IsDrm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("No output is connected. Connect a monitor, then start the greeter again.");
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += () => Stop("parent");
        }

        _reader = new ScreenReader(_processes, _options.Settings.Orca, _options.Settings.ScreenReader, _log);
        _reader.Started += process => BasinReport.Line($"READER on pid={process.Pid}");
        _reader.StartFailed += _ => BasinReport.Line("READER failed");
        _reader.Stopped += () => BasinReport.Line("READER off");
        var aids = _seat.Binder.UseKeyboardAids(_host.Loop);
        aids.Options = _options.Settings.KeyboardAids;
        _access = new Basin.Accessibility.DesktopAccessibility(new Basin.Accessibility.DesktopAccessibilityOptions
        {
            Name = _options.Program,
            Host = _host,
            Scene = _scene,
            Seat = _seatState,
            Pointer = _seat.Binder.Pointer,
            Processes = _processes,
            TextInput = _services.Find<Basin.Desktop.TextInputManager>(),
            Aids = aids,
            Reader = _reader,
            StartToolkit = _ui is AvaloniaUIHost avalonia
                ? avalonia.StartAccessibilityAsync
                : static () => Task.FromResult<string?>("the greeter's toolkit has no accessibility tree"),
            Log = _log,
        });
        _cursor.Curtain = _access.Curtain;
    }

    private Basin.Accessibility.DesktopAccessibility? _access;

    /// <summary>
    /// Gets the greeter's accessibility suite: the reader, zoom, the curtain and the keyboard aids. It is <see langword="null"/> before the host starts.
    /// </summary>
    public Basin.Accessibility.DesktopAccessibility? Accessibility => _access;

    /// <summary>
    /// Starts the accessibility tree in the background and prints <c>A11Y on</c> or <c>A11Y off</c>. A host that is not Avalonia always prints <c>A11Y off</c>.
    /// </summary>
    public void StartAccessibility() => _ = StartAccessibilityTreeAsync();

    private async Task StartAccessibilityTreeAsync()
    {
        if (_access is { } access)
        {
            await access.StartAsync(AccessibilityMode.On);
        }
    }

    /// <summary>
    /// Stops the loop. The first call sets the exit reason and status. The reason <c>start</c> marks a session start for the handoff.
    /// </summary>
    /// <param name="reason">The word for the <c>EXIT</c> line, for example <c>quit</c>.</param>
    /// <param name="status">The exit status that <see cref="Run"/> returns. Nonzero marks a failure for the handoff.</param>
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

    /// <summary>
    /// Renders the scene of the first output to a PNG file. It does nothing when the path is empty or no output exists.
    /// </summary>
    /// <param name="path">The file to write, or <see langword="null"/>.</param>
    public void WriteScreenshot(string? path)
    {
        if (path is not { Length: > 0 } || _outputs.Views.FirstOrDefault() is not { } view)
        {
            return;
        }

        _uiDriver.Pump();
        if (SceneScreenshot.Write(_scene, _renderer, view.Output, path))
        {
            _log.Info($"screenshot: written to {path}");
        }
    }

    /// <summary>
    /// Runs the loop until a stop. Before the loop it prints <c>RENDERER</c> and <c>SOCKET</c>, starts the control socket and shows the user list. After the loop it prints <c>EXIT</c>.
    /// </summary>
    /// <param name="beforeCommands">An action that runs after the UI driver starts and before the control socket starts, or <see langword="null"/>.</param>
    /// <returns>The exit status that <see cref="Stop"/> set, zero by default.</returns>
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
        return _loop.ExitCode != 0 ? _loop.ExitCode : _status;
    }

    /// <summary>
    /// Places the login box on the active output and raises <see cref="BoxPlaced"/>. It does nothing when no active output has a view.
    /// </summary>
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


    private bool AccessChord(uint key, bool pressed)
    {
        if (key == KeyS || _access is not { } access || Basin.Accessibility.DesktopAccessibility.DefaultActionForKey(key) is not { } action ||
            _seatState.Keyboard.Keymap is not { } keymap)
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
            access.Run(action);
        }

        return true;
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

    /// <summary>
    /// Disconnects the clients, ends the child processes with a 500 ms grace, and disposes the flow, the greetd connection and every part of the compositor.
    /// </summary>
    public void Dispose()
    {
        _host.DisconnectClients();
        _ipc?.Dispose();
        _settle?.Dispose();
        _access?.Dispose();
        _access = null;
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
