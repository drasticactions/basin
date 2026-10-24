using System.Globalization;
using Avalonia.Media.Imaging;
using Basin;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Greetd;
using Basin.Greetd.Host;
using Basin.Host;
using Basin.Scene;
using Basin.UI.Avalonia;

namespace LunaWm;

internal sealed partial class LunaGreeter : IDisposable
{
    private readonly LunaGreeterOptions _options;
    private readonly BasinLogger _log;
    private readonly GreeterHost _greeter;
    private readonly IRenderer _renderer;
    private readonly BasinHost _host;
    private readonly OutputLayout _layout;
    private readonly SceneTree _barLayer;
    private readonly SceneTree _menuLayer;
    private readonly BasinServices _services;
    private readonly OutputDriver _outputs;
    private readonly AvaloniaUIHost _ui;
    private readonly UIDriver _uiDriver;
    private readonly UISurfaceIndex _index;
    private readonly ChromeSeat _seat;
    private readonly ActiveOutputTracker _active;
    private readonly Dictionary<IOutput, (OutputUISurface Surface, GreeterBackgroundModel Model)> _backgrounds = [];
    private readonly OutputUISurface _box;
    private readonly GreeterLoginModel _model = new();
    private readonly GreeterMemory _memory;
    private LunaGreeterConfig _config;
    private Bitmap? _wallpaper;
    private GreeterLoginView? _loginView;
    private IEventSource? _clockTimer;
    private string? _clockOverride;

    public static int Run(LunaGreeterOptions options, BasinLogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var greeter = new LunaGreeter(options, log);
            status = greeter.RunLoop();
            rendered = greeter._outputs.PrimaryRendered;
            greeter._greeter.WriteScreenshot(options.Screenshot);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException or System.Net.Sockets.SocketException)
        {
            log.Error(error, $"startup: the greeter did not start");
            return 1;
        }

        BasinReport.Line(CompositorLines.Frames(rendered));
        if (CompositorLines.Census() is { } census)
        {
            log.Error($"{census}");
        }

        return status;
    }

    private LunaGreeter(LunaGreeterOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;
        _config = LunaGreeterConfig.Load(options.ConfigPath, log);
        _memory = GreeterMemory.Load(options.StatePath ?? GreeterMemory.DefaultPath(StateProgram), log);
        _greeter = GreeterHost.Create<LunaApp>(new GreeterHostOptions
        {
            Program = StateProgram,
            Backend = options.Backend.ToString().ToLowerInvariant(),
            Handoff = options.Handoff,
            Renderer = !options.RendererGiven && _config.Renderer.Length > 0 ? _config.Renderer : options.Renderer,
            Outputs = options.Outputs,
            Scales = options.Scales,
            Frames = options.Frames,
            SocketFd = options.SocketFd,
            HeadlessMode = new OutputMode(1024, 768, 60_000),
            GreetdSocket = options.GreetdSocket,
            Demo = options.Demo,
            Settings = _config.Settings,
            Memory = _memory,
            FlowOptions = new GreeterFlowOptions
            {
                Root = GreeterFlowOptions.RootFromEnvironment("BASIN_GREETER_ROOT"),
                Log = log,
            },
            IpcListen = options.Ipc.Listen,
            IpcPath = options.Ipc.Path,
            CommandNamespace = "lunawm",
            Theme = UIThemeVariant.Light,
            Configure = builder => AvaEnyo.AppBuilderExtensions.WithAvaEnyoFonts(builder),
        }, log);
        _renderer = _greeter.Renderer;
        _host = _greeter.Host;
        _layout = _greeter.Layout;
        _services = _greeter.Services;
        _outputs = _greeter.Outputs;
        _ui = _greeter.Ui;
        _uiDriver = _greeter.UiDriver;
        _index = _greeter.Index;
        _active = _greeter.Active;
        _box = _greeter.Box;
        _barLayer = _greeter.AddLayer();
        _menuLayer = _greeter.AddLayer();
        _wallpaper = LoadImage(_config.Wallpaper, 0, log);

        _greeter.BackgroundAdded += OnBackgroundAdded;
        _greeter.BackgroundRemoved += output => _backgrounds.Remove(output);
        _greeter.BoxMoved += _ => LayoutKeyboard();
        _greeter.BoxPlaced += PlaceChrome;
        _greeter.BoxRealized += surface =>
        {
            _loginView = new GreeterLoginView { DataContext = _model };
            ((AvaloniaUISurface)surface).Content = _loginView;
            ((AvaloniaUISurface)surface).AccessibleName = "Log in";
            AttachKeyboardView();
        };
        _greeter.Pressed = () => _keyboardPolicy?.NotePointer();
        _greeter.KeyHook = OnKey;
        _greeter.Reload = Reload;
        _greeter.Submit = () => _model.Submit.Execute(null);
        _greeter.ClockOverride = SetClockOverride;
        _greeter.Where = PrintState;
        _greeter.MoreCommands = Register;
        _greeter.Start();
        _seat = _greeter.Seat;

        WireLogin();
        WireStatus();
        WireKeyboard();
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GreeterLoginModel.KeyboardVisible))
            {
                foreach (var (_, background) in _backgrounds.Values)
                {
                    background.ClockShown = !_model.KeyboardVisible;
                }
            }
        };
        WireAccessibility();
    }

    internal const string StateProgram = "luna-wm-greeter";

    internal void Stop(string reason, int status = 0) => _greeter.Stop(reason, status);

    internal void WriteScreenshot(string? path) => _greeter.WriteScreenshot(path);

    private int RunLoop()
    {
        var status = _greeter.Run(() =>
        {
            _clockTimer = _host.Loop.AddTimer(OnClockTick);
            OnClockTick();
        });
        _clockTimer?.Remove();
        _clockTimer = null;
        return status;
    }

    private void OnClockTick()
    {
        var now = DateTime.Now;
        var clock = _clockOverride ?? now.ToString(_config.Clock24 ? "H:mm" : "h:mm tt", CultureInfo.InvariantCulture);
        var date = _clockOverride is not null ? "Monday, January 2" : now.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
        foreach (var (_, model) in _backgrounds.Values)
        {
            model.Clock = clock;
            model.Date = date;
        }

        if (_bar is { } bar)
        {
            bar.Model.Clock = clock;
        }

        _clockTimer?.UpdateTimer(Math.Max(50, ((60 - now.Second) * 1000) - now.Millisecond));
    }

    internal void SetClockOverride(string? text)
    {
        _clockOverride = text is { Length: > 0 } ? text : null;
        OnClockTick();
    }

    private void OnBackgroundAdded(IOutput output, OutputUISurface surface)
    {
        surface.AccessibleName = "Background";
        var model = new GreeterBackgroundModel { Wallpaper = _wallpaper, HighContrast = _highContrast, ClockShown = !_model.KeyboardVisible };
        surface.Realized += realized => ((AvaloniaUISurface)realized).Content = new GreeterBackgroundView { DataContext = model };
        _backgrounds[output] = (surface, model);
        OnClockTick();
    }

    private void PlaceBox() => _greeter.PlaceBox();

    internal void Reload()
    {
        if (_options.ConfigPath is null)
        {
            BasinReport.Line("RELOAD skipped no-config");
            return;
        }

        var next = LunaGreeterConfig.Load(_options.ConfigPath, _log);
        if (next.Failed)
        {
            BasinReport.Line("RELOAD failed");
            return;
        }

        var restart = string.Join(' ', _config.RestartKeysChanged(next));
        _config = next;
        _wallpaper?.Dispose();
        _wallpaper = LoadImage(_config.Wallpaper, 0, _log);
        foreach (var (_, model) in _backgrounds.Values)
        {
            model.Wallpaper = _wallpaper;
        }

        OnClockTick();
        if (_reader is { } reader)
        {
            reader.Argv = _config.Settings.Orca;
            reader.Mode = _config.Settings.ScreenReader;
        }

        ApplyAccessibilityDefaults();
        if (_indicators is { } indicators)
        {
            indicators.JoinAllowed = _config.Settings.NetworkJoin;
        }

        _flow?.Reload(_config.Settings);
        PlaceBox();
        _outputs.ScheduleAll();
        BasinReport.Line(restart.Length > 0 ? $"RELOAD restart-needed {restart}" : "RELOAD ok");
    }

    private static Bitmap? LoadImage(string? path, int width, BasinLogger log)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            if (width <= 0)
            {
                return new Bitmap(path);
            }

            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            log.Warn(error, $"look: cannot load the image {path}");
            return null;
        }
    }

    public void Dispose()
    {
        _clockTimer?.Remove();
        StopKeyboard();
        _indicators?.Dispose();
        _menu?.Dispose();
        _bar?.Dispose();
        _greeter.Dispose();
        _backgrounds.Clear();
        _wallpaper?.Dispose();
    }
}
