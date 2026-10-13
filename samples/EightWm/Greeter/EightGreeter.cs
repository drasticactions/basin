using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Basin;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Greetd;
using Basin.Greetd.Host;
using Basin.Host;
using Basin.Scene;
using Basin.UI.Avalonia;

namespace EightWm;

internal sealed partial class EightGreeter : IDisposable
{
    internal const string StateProgram = "eight-wm-greeter";

    private readonly EightGreeterOptions _options;
    private readonly BasinLogger _log;
    private readonly GreeterHost _greeter;
    private readonly BasinHost _host;
    private readonly OutputLayout _layout;
    private readonly OutputDriver _outputs;
    private readonly UIDriver _uiDriver;
    private readonly ActiveOutputTracker _active;
    private readonly OutputUISurface _box;
    private readonly ChromeSeat _seat;
    private readonly GreeterMemory _memory;
    private readonly Dictionary<IOutput, CurtainPair> _backgrounds = [];
    private readonly SignInModel _signIn = new();
    private readonly CurtainModel _curtain = new();
    private readonly NetworkModel _network = new();
    private readonly HashSet<uint> _curtainKeys = [];
    private EightGreeterConfig _config;
    private Bitmap? _picture;
    private GreeterScreen? _screen;
    private IEventSource? _clockTimer;
    private string? _clockOverride;

    public static int Run(EightGreeterOptions options, BasinLogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var greeter = new EightGreeter(options, log);
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

    private EightGreeter(EightGreeterOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;
        _config = EightGreeterConfig.Load(options.ConfigPath, log);
        _memory = GreeterMemory.Load(options.StatePath ?? GreeterMemory.DefaultPath(StateProgram), log);
        _greeter = GreeterHost.Create<EightWmApp>(new GreeterHostOptions
        {
            Program = StateProgram,
            Backend = options.Backend.ToString().ToLowerInvariant(),
            Renderer = !options.RendererGiven && _config.Renderer.Length > 0 ? _config.Renderer : options.Renderer,
            Outputs = options.Outputs,
            Scales = options.Scales,
            Frames = options.Frames,
            SocketFd = options.SocketFd,
            HeadlessMode = new OutputMode(1366, 768, 60_000),
            GreetdSocket = options.GreetdSocket,
            Demo = options.Demo,
            Settings = _config.Settings,
            Memory = _memory,
            FlowOptions = new GreeterFlowOptions
            {
                Root = GreeterFlowOptions.RootFromEnvironment("BASIN_GREETER_ROOT"),
                LoginIncorrect = "The password is incorrect. Try again.",
                Log = log,
            },
            IpcListen = options.Ipc.Listen,
            IpcPath = options.Ipc.Path,
            CommandNamespace = "eightwm",
            Theme = _config.Dark ? UIThemeVariant.Dark : UIThemeVariant.Light,
            Configure = builder => AvaWin.AppBuilderExtensions.WithAvaWinFonts(builder),
        }, log);
        _host = _greeter.Host;
        _layout = _greeter.Layout;
        _outputs = _greeter.Outputs;
        _uiDriver = _greeter.UiDriver;
        _active = _greeter.Active;
        _box = _greeter.Box;
        ApplyLook();

        _greeter.BackgroundAdded += OnBackgroundAdded;
        _greeter.BackgroundRemoved += output => _backgrounds.Remove(output);
        _greeter.BoxMoved += _ => LayoutKeyboard();
        _greeter.BoxRealized += surface =>
        {
            _screen = new GreeterScreen(_signIn, _curtain, _network);
            ((AvaloniaUISurface)surface).Content = _screen;
            ((AvaloniaUISurface)surface).AccessibleName = "Sign in";
            AttachKeyboardView();
            if (!_curtain.Raised)
            {
                _screen.Curtain.Focus();
            }
        };
        _greeter.Pressed = () => _keyboardPolicy?.NotePointer();
        _greeter.KeyHook = OnKey;
        _greeter.Reload = Reload;
        _greeter.Submit = () => _signIn.Submit.Execute(null);
        _greeter.ClockOverride = SetClockOverride;
        _greeter.Where = PrintState;
        _greeter.MoreCommands = Register;
        _greeter.Start();
        _seat = _greeter.Seat;

        _curtain.RaiseRequested += how => RaiseCurtain(how);
        _curtain.NetworkRequested += () => _network.Open = !_network.Open && _network.CanJoin;
        WireLogin();
        WireStatus();
        WireKeyboard();
        WireAccessibility();
    }

    internal void Stop(string reason, int status = 0) => _greeter.Stop(reason, status);

    private bool OnKey(uint time, uint key, bool pressed)
    {
        if (key is 29 or 97 or 42 or 54 or 56 or 100 or 125 or 126 or 58)
        {
            return false;
        }

        if (!_curtain.Raised)
        {
            if (pressed)
            {
                _keyboardPolicy?.NoteHardwareKey();
                _curtainKeys.Add(key);
                RaiseCurtain("key");
            }

            return true;
        }

        if (pressed)
        {
            _keyboardPolicy?.NoteHardwareKey();
        }

        return !pressed && _curtainKeys.Remove(key);
    }

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

    internal void RaiseCurtain(string how)
    {
        if (_curtain.Raised)
        {
            return;
        }

        _curtain.Raised = true;
        foreach (var pair in _backgrounds.Values)
        {
            pair.Over.Raised = true;
        }

        _network.Open = false;
        BasinReport.Line($"CURTAIN up {how}");
        if (how == "swipe")
        {
            _keyboardPolicy?.NoteTouch();
        }

        _signIn.RequestFocus();
        if (_config.Settings.OnScreen == "auto")
        {
            _keyboardPolicy?.Present();
        }

        _outputs.ScheduleAll();
    }

    internal void LowerCurtain()
    {
        if (!_curtain.Raised)
        {
            return;
        }

        _curtain.Raised = false;
        foreach (var pair in _backgrounds.Values)
        {
            pair.Over.Raised = false;
        }

        _keyboardPolicy?.Hide("curtain");
        ShowKeyboard(false);
        BasinReport.Line("CURTAIN down");
        _screen?.Curtain.Focus();
        _outputs.ScheduleAll();
    }

    private void OnBackgroundAdded(IOutput output, OutputUISurface surface)
    {
        var under = NewCurtain(dimmed: true);
        var over = NewCurtain(dimmed: false);
        over.Raised = _curtain.Raised;
        var pair = new CurtainPair(under, over);
        surface.Realized += realized => ((AvaloniaUISurface)realized).Content = pair;
        _backgrounds[output] = pair;
        OnClockTick();
    }

    private CurtainModel NewCurtain(bool dimmed) => new()
    {
        Picture = _picture,
        Fill = AccentFill(),
        Dimmed = dimmed,
        HighContrast = _highContrast,
        Clock = _curtain.Clock,
        Date = _curtain.Date,
    };

    private IEnumerable<CurtainModel> Curtains()
    {
        yield return _curtain;
        foreach (var pair in _backgrounds.Values)
        {
            yield return pair.Under;
            yield return pair.Over;
        }
    }

    private void OnClockTick()
    {
        var now = DateTime.Now;
        var clock = _clockOverride ?? now.ToString(_config.Clock24 ? "H:mm" : "h:mm", CultureInfo.InvariantCulture);
        var date = _clockOverride is not null ? "Monday, January 2" : now.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
        foreach (var curtain in Curtains())
        {
            curtain.Clock = clock;
            curtain.Date = date;
        }

        _clockTimer?.UpdateTimer(Math.Max(50, ((60 - now.Second) * 1000) - now.Millisecond));
    }

    internal void SetClockOverride(string? text)
    {
        _clockOverride = text is { Length: > 0 } ? text : null;
        OnClockTick();
    }

    private Color AccentColor => Color.FromUInt32(_config.Accent);

    private IBrush AccentFill() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(AccentColor, 0),
            new GradientStop(Color.FromRgb((byte)(AccentColor.R * 0.5), (byte)(AccentColor.G * 0.5), (byte)(AccentColor.B * 0.5)), 1),
        },
    };

    private void ApplyLook()
    {
        _picture = LoadImage(_config.Picture, _log);
        if (Application.Current is { } app)
        {
            StillCaret.Apply(app);
            SignInTheme.Install(app, AccentColor);
            app.Resources["SystemAccentColor"] = AccentColor;
            SignInTheme.SetHighContrast(app, _highContrast, NormalVariant);
        }

        foreach (var curtain in Curtains())
        {
            curtain.Picture = _picture;
            curtain.Fill = AccentFill();
        }
    }

    private ThemeVariant NormalVariant => _config.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    internal void Reload()
    {
        if (_options.ConfigPath is null)
        {
            BasinReport.Line("RELOAD skipped no-config");
            return;
        }

        var next = EightGreeterConfig.Load(_options.ConfigPath, _log);
        if (next.Failed)
        {
            BasinReport.Line("RELOAD failed");
            return;
        }

        var restart = string.Join(' ', _config.RestartKeysChanged(next));
        _config = next;
        var old = _picture;
        ApplyLook();
        old?.Dispose();
        OnClockTick();
        if (_greeter.Reader is { } reader)
        {
            reader.Argv = _config.Settings.Orca;
            reader.Mode = _config.Settings.ScreenReader;
        }

        ApplyAccessibilityDefaults();
        _network.CanJoin = _config.Settings.NetworkJoin;
        _greeter.FlowOrNull?.Reload(_config.Settings);
        ShowLayoutList();
        _greeter.PlaceBox();
        _outputs.ScheduleAll();
        BasinReport.Line(restart.Length > 0 ? $"RELOAD restart-needed {restart}" : "RELOAD ok");
    }

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
        _clockTimer?.Remove();
        StopKeyboard();
        DisposeStatus();
        _context?.Dispose();
        _greeter.Dispose();
        _backgrounds.Clear();
        _picture?.Dispose();
    }
}
