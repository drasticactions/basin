using Avalonia.Media;
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
using BasinGreeter.UI;

namespace BasinGreeter;

internal sealed partial class Greeter : IDisposable
{
    private readonly GreeterOptions _options;
    private readonly BasinLogger _log;
    private readonly GreeterHost _greeter;
    private readonly IRenderer _renderer;
    private readonly string _rendererName;
    private readonly BasinHost _host;
    private readonly OutputLayout _layout;
    private readonly BasinServices _services;
    private readonly OutputDriver _outputs;
    private readonly AvaloniaUIHost _ui;
    private readonly UIDriver _uiDriver;
    private readonly ActiveOutputTracker _active;
    private readonly OutputUISurface _box;
    private readonly ChromeSeat _seat;
    private readonly Dictionary<IOutput, (OutputUISurface Surface, BackgroundModel Model)> _backgrounds = [];
    private readonly LoginModel _model = new();
    private readonly Bitmap? _backgroundImage;
    private GreeterConfig _config;
    private readonly GreeterMemory _state;
    private ShellClock? _clock;

    public static int Run(GreeterOptions options, BasinLogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var greeter = new Greeter(options, log);
            status = greeter.RunLoop();
            rendered = greeter._outputs.PrimaryRendered;
            greeter._greeter.WriteScreenshot(options.Screenshot);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException or System.Net.Sockets.SocketException)
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

    private Greeter(GreeterOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;
        _config = GreeterConfig.Load(options.ConfigPath, log);
        _state = GreeterMemory.Load(options.StatePath ?? GreeterMemory.DefaultPath("basin-greeter"), log);
        _greeter = GreeterHost.Create<GreeterApp>(new GreeterHostOptions
        {
            Program = "basin-greeter",
            Backend = options.Backend.ToString().ToLowerInvariant(),
            Renderer = !options.RendererGiven && _config.Renderer.Length > 0 ? _config.Renderer : options.Renderer,
            Outputs = options.Outputs,
            Scales = options.Scales,
            Frames = options.Frames,
            SocketFd = options.SocketFd,
            HeadlessMode = new OutputMode(1280, 720, 60_000),
            GreetdSocket = options.GreetdSocket,
            Demo = options.Demo,
            DemoOptions = new LoopbackGreetdOptions { Info = GreeterText.DemoInfo, Prompt = GreeterText.DemoPassword },
            Settings = _config.Settings,
            Memory = _state,
            FlowOptions = new GreeterFlowOptions
            {
                Root = GreeterFlowOptions.RootFromEnvironment("BASIN_GREETER_ROOT"),
                LoginIncorrect = GreeterText.LoginIncorrect,
                NoSessions = GreeterText.NoSessions,
                Log = log,
            },
            IpcListen = options.Ipc.Listen,
            IpcPath = options.Ipc.Path,
            CommandNamespace = "greeter",
            Theme = ThemeOf(_config),
        }, log);
        _renderer = _greeter.Renderer;
        _rendererName = _greeter.RendererName;
        _host = _greeter.Host;
        _layout = _greeter.Layout;
        _services = _greeter.Services;
        _outputs = _greeter.Outputs;
        _ui = _greeter.Ui;
        _uiDriver = _greeter.UiDriver;
        _active = _greeter.Active;
        _box = _greeter.Box;
        ApplyLook();
        _backgroundImage = LoadImage(_config.Background, log);

        _greeter.BackgroundAdded += OnBackgroundAdded;
        _greeter.BackgroundRemoved += output => _backgrounds.Remove(output);
        _greeter.BoxMoved += _ => LayoutKeyboard();
        _greeter.BoxRealized += surface =>
        {
            _loginView = new LoginView { DataContext = _model };
            ((AvaloniaUISurface)surface).Content = _loginView;
            ((AvaloniaUISurface)surface).AccessibleName = GreeterText.LoginWindow;
            AttachKeyboardView();
        };
        _greeter.Pressed = () => _keyboardPolicy?.NotePointer();
        _greeter.KeyHook = OnKey;
        _greeter.Reload = Reload;
        _greeter.Submit = () => _model.Submit.Execute(null);
        _greeter.ClockOverride = text =>
        {
            if (_clock is { } clock)
            {
                clock.Frozen = text;
                clock.Refresh();
            }
        };
        _greeter.Where = PrintState;
        _greeter.MoreCommands = Register;
        _greeter.Start();
        _seat = _greeter.Seat;

        WireLogin();
        WireKeyboard();
        WireAccessibility();
        WireReader();
    }

    internal void Stop(string reason, int status = 0) => _greeter.Stop(reason, status);

    internal void WriteScreenshot(string? path) => _greeter.WriteScreenshot(path);

    private int RunLoop()
    {
        var status = _greeter.Run(() =>
        {
            _clock = new ShellClock(_host.Loop, now => ClockFormat.Format(_config.Clock, now), text =>
            {
                foreach (var (_, model) in _backgrounds.Values)
                {
                    model.Clock = text ?? "";
                }
            })
            {
                PerSecond = () => ClockFormat.PerSecond(_config.Clock),
            };
            _clock.Refresh();
        });
        return status;
    }

    private void OnBackgroundAdded(IOutput output, OutputUISurface surface)
    {
        var model = new BackgroundModel
        {
            Fill = new SolidColorBrush(_highContrast ? Colors.Black : Argb(_config.BackgroundColor)),
            Image = _highContrast ? null : _backgroundImage,
            Clock = _clock?.Frozen ?? "",
        };
        surface.Realized += realized => ((AvaloniaUISurface)realized).Content = new BackgroundView { DataContext = model };
        _backgrounds[output] = (surface, model);
        _clock?.Refresh();
    }

    private void PlaceBox() => _greeter.PlaceBox();

    internal void Reload()
    {
        if (_options.ConfigPath is null)
        {
            BasinReport.Line("RELOAD skipped no-config");
            return;
        }

        var next = GreeterConfig.Load(_options.ConfigPath, _log);
        if (next.Failed)
        {
            BasinReport.Line("RELOAD failed");
            return;
        }

        var restart = string.Join(' ', _config.RestartKeysChanged(next));
        _config = next;
        _ui.Theme = ThemeOf(_config);
        ApplyLook();
        foreach (var (_, model) in _backgrounds.Values)
        {
            model.Fill = new SolidColorBrush(Argb(_config.BackgroundColor));
        }

        ApplyAccessibilityDefaults();
        if (_greeter.Reader is { } reader)
        {
            reader.Argv = _config.Settings.Orca;
            reader.Mode = _config.Settings.ScreenReader;
        }

        _clock?.Refresh();
        _model.AllowCommand = _config.Settings.AllowCommand;
        ShowLayoutList();
        _flow?.Reload(_config.Settings);

        PlaceBox();
        _outputs.ScheduleAll();
        BasinReport.Line(restart.Length > 0 ? $"RELOAD restart-needed {restart}" : "RELOAD ok");
    }

    private void ApplyLook()
    {
        if (Avalonia.Application.Current is { } app)
        {
            app.Resources["SystemAccentColor"] = _highContrast ? UI.HighContrastTheme.Focus : Argb(_config.Accent);
        }
    }

    private static UIThemeVariant ThemeOf(GreeterConfig config) => config.Theme == "light" ? UIThemeVariant.Light : UIThemeVariant.Dark;

    private static Color Argb(uint value) => Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    private static Bitmap? LoadImage(string? path, BasinLogger log)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            log.Warn($"cannot load {path}: {error.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _clock?.Dispose();
        _keyboardPolicy?.Dispose();
        _keyboard?.Dispose();
        DisposeStatus();
        _context?.Dispose();
        _greeter.Dispose();
        _backgrounds.Clear();
        _backgroundImage?.Dispose();
    }
}
