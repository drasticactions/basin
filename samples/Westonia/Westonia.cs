using System.Diagnostics;
using Avalonia;
using Basin;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Weston;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;
using Microsoft.Extensions.Logging;
using Wayland.Server;

namespace Westonia;

internal sealed partial class Westonia : IDisposable
{
    private readonly WestoniaOptions _options;
    private readonly ILogger _log;
    private readonly WestonIni _ini;
    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly Basin.Host.BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private readonly BasinServices _services;
    private readonly ShellLayers _layers;
    private readonly OutputDriver _outputs;
    private readonly AvaloniaUIHost _ui;
    private readonly AvaloniaShell _avalonia;
    private readonly WestonShell _shell;
    private readonly OutputScreens _screens;
    private readonly UISurfaceIndex _shellSurfaces = new();
    private Basin.Desktop.FractionalScaleManager? _fractionalScale;
    private readonly Basin.Desktop.CursorController _cursor;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private WestoniaSeat? _seat;
    private ShellSwitcher? _switcher;
    private bool _decorate = true;
    private ShellWorkspaces? _workspaces;
    private ShellLock? _lock;
    private ShellAnimations? _animations;
    private ShellXWayland? _xwayland;
    private Basin.XWayland.XWaylandServer? _xServer;
    private IEventSource? _idleTimer;
    private StdinCommands? _stdinCommands;
    private System.Diagnostics.Process? _screensaver;
    private readonly DeferredWorkspaceModel _deferredWorkspaces = new();
    private readonly List<Process> _spawned = [];
    private readonly UIDriver _uiDriver;
    private IEventSource? _clockTimer;
    private bool _running = true;

    public static int Run(WestoniaOptions options, ILogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var westonia = new Westonia(options, log);
            status = westonia.RunLoop();
            rendered = westonia._outputs.PrimaryRendered;
            westonia.WriteScreenshot();
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException)
        {
            log.LogError("{Reason}", error.Message);
            return 1;
        }

        Console.WriteLine(
            $"FRAMES {rendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}");
        if (BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0))
        {
            BasinCounters.WriteCensus(Console.Error);
        }

        return status;
    }

    internal Westonia(WestoniaOptions options, ILogger log)
    {
        _options = options;
        _log = log;
        _ini = options.NoConfig ? new WestonIni() : WestonIni.Load(options.ConfigPath, log);

        var rendererName = _ini.Core.Renderer is { Length: > 0 } fromIni && options.Renderer == "vulkan"
            ? fromIni
            : options.Renderer;
        var stack = CreateStack(rendererName, log);
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;

        _host = Basin.Host.BasinHost.Create(new Basin.Host.HostOptions
        {
            Backend = options.Backend switch
            {
                BackendKind.Drm => Basin.Host.HostBackend.Drm,
                BackendKind.Nested => Basin.Host.HostBackend.Nested,
                _ => Basin.Host.HostBackend.Headless,
            },
            SocketFd = options.SocketFd,
        });

        var capturePack = new SceneCapturePack(_scene, _layout);
        capturePack.Capture.Renderer = _renderer;
        var cursorTheme = new Basin.Capabilities.Defaults.CursorImageTheme();
        _cursor = new Basin.Desktop.CursorController(_layout) { Capture = capturePack.Capture };
        if (_host.Session is { } session && options.Backend == BackendKind.Drm)
        {
            _input = new Basin.Backend.Libinput.LibinputBackend(_host.Loop, session);
        }

        _services = _host.CreateServices()
            .Use(_layout)
            .With(capturePack)
            .With(new DrmCapabilityPack(_renderer, _host.Drm))
            .Use<ICursorTheme>(cursorTheme)
            .Use<IActivationTokens>(new Basin.Capabilities.Defaults.DefaultActivationTokens())
            .Use<IBell>(Basin.Capabilities.Defaults.SilentBell.Instance)
            .Use<IColorProfileService>(new Basin.Color.Lcms2ColorProfileService());

        var pack = DesktopPack.For("westonia");
        if (_host.Drm is null)
        {
            pack = pack.Without("wp_drm_lease_device_v1");
        }

        _services.Install(pack);

        Basin.XWayland.XWaylandModule? xwaylandModule = null;
        if (_ini.Core.XWayland || options.XWayland)
        {
            xwaylandModule = new Basin.XWayland.XWaylandModule();
            _services.Install(xwaylandModule);
        }

        if (_renderer.Device is { } renderDevice)
        {
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath));
        }

        _layers = new ShellLayers(_scene.Root);
        _shell = new WestonShell(_layers, _ini, log);

        if (_ini.Shell.Client is { Length: > 0 })
        {
            _services.Use<IShellRoles>(new ShellRolesImpl(_shell));
            _services.Install(WestonShellPack.Create(pid => _spawned.Any(p => p.Id == pid)));
        }

        _services.Use<IWorkspaceModel>(_deferredWorkspaces);
        _services.Freeze();
        _shell.Client = _services.Find<IShellClient>();

        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = capturePack,
            Frames = _services.Require<IFrameClock>(),
            Requested = options.Outputs,
            Scales = options.Scales,
            ContinuousRepaint = options.Frames > 0,
            NestedName = index => $"westonia-{index + 1}",
            HeadlessMode = new OutputMode(1280, 720, 60_000),
        };
        _outputs.Emptied += Stop;
        _outputs.ModesetRefused += card =>
            log.LogError("modeset refused by {Output} in every mode", card.Name);
        _outputs.Added += view => Console.WriteLine(
            $"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        _outputs.Added += OnOutputAdded;
        _outputs.Removed += OnOutputRemoved;
        _outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene);
        _outputs.Added += DescribeOutput;
        _outputs.Removed += view => _cursor.RemoveOutput(view.Output);
        _outputs.LayoutChanged += PlaceShell;
        _outputs.BeforeRepaint += view =>
        {
            var tick = new FrameTick(
                view.Scheduler?.PredictedVblankNanos ?? 0,
                view.Output.CurrentMode.RefreshIntervalNanoseconds is var interval and > 0 ? interval : 16_666_666L);
            _animations?.Step(tick);
            if (_animations is { IsRunning: true })
            {
                view.Scheduler?.ScheduleRepaint();
            }

            UpdateSurfacePresence();
            _workspaces?.Step(view.Scheduler?.PredictedVblankNanos ?? 0);
            if (_workspaces is { IsSliding: true })
            {
                view.Scheduler?.ScheduleRepaint();
            }

            SyncPopups();
        };
        _screens = new OutputScreens(_outputs, _layout);
        _ui = BasinPlatform.Start<Shell.ShellApp>(new BasinPlatformOptions
        {
            EventLoop = _host.Loop,
            Screens = _screens,
            Selection = _services.Find<ISelectionStore>(),
            Theme = options.Theme,
        });
        _avalonia = new AvaloniaShell(_ui, _layers, _ini, log, Spawn, _shellSurfaces)
        {
            PanelPosition = _ini.Shell.PanelPosition,
        };
        _shell.Avalonia = _avalonia;
        _uiDriver = new UIDriver(_ui, _host.Loop)
        {
            PopupLayer = _layers.Panel,
            Index = _shellSurfaces,
        };

        Seat = _services.Require<Basin.Seat.Seat>();
        Shell = _services.Require<XdgShell>();
        _fractionalScale = _services.Find<Basin.Desktop.FractionalScaleManager>();
        WireColor();
        Decorations = _services.Require<XdgDecorationManager>();
        Decorations.DefaultMode = DecorationMode.ClientSide;
        Decorations.ChooseMode = (_, preference) => preference ?? DecorationMode.ServerSide;

        _outputs.CreateInitialOutputs();
        if (_host.Parent is not null)
        {
            _cursor.UseParentCursor();
        }

        _cursor.ColorProfiles = _services.Find<IColorProfileService>();
        if (_services.Find<Basin.Desktop.CursorShapeManager>() is { } shapes)
        {
            _cursor.Shapes = shapes;
            shapes.CursorRequested += _cursor.ShowImage;
        }

        Seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;
        _cursor.Load(new ShmAllocator(), 64, 64, _ini.Shell.CursorSize);
        cursorTheme.Images = _cursor.Images;
        Console.WriteLine($"CURSOR {_cursor.Showing} {_cursor.Images?.Size ?? 0}px {_cursor.DrawnBy}");
        ApplyKeyboardConfig();
        ApplyLibinputConfig();
        ApplyOutputConfig();
        _seat = new WestoniaSeat(
            _host, _services, _layout, _scene, _cursor, _avalonia, _shellSurfaces, _shell, _input, log)
        {
            KeyHook = OnKeyBinding,
            ButtonHook = OnButtonBinding,
            FrameButtonHook = OnFrameButton,
            FrameCursorAt = FrameCursorName,
        };
        _seat.PointerMoved = position => _shell.UpdateGrab(position.X, position.Y);
        _shell.Input = _seat.Router;

        _bindingModifier = BindingModifiers.Parse(_ini.Shell.BindingModifier);
        _workspaces = new ShellWorkspaces(_layers, _shell, _ini.Shell.NumWorkspaces)
        {
            Changed = () => _outputs.ScheduleAll(),
            OutputHeight = () => _outputs.Views.Count > 0 ? _layout.BoxOf(_outputs.Views[0].Output).Height : 720,
            Outputs = outputs =>
            {
                var count = Math.Min(outputs.Length, _outputs.Views.Count);
                for (var i = 0; i < count; i++)
                {
                    outputs[i] = _outputs.Views[i].Output;
                }

                return count;
            },
        };
        _lock = new ShellLock(_ui, _layers, _shell, _ini, log, PrimaryBox, _shellSurfaces)
        {
            Changed = () =>
            {
                _animations?.BeginSessionFade(toBlack: false);
                _outputs.ScheduleAll();
            },
            LayoutOf = output => _layout.BoxOf(output),
            Scale = () => _outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0,
        };
        if (_services.Find<Basin.Desktop.SessionLockManager>() is { } sessionLock)
        {
            _lock.AttachSessionLock(sessionLock);
        }

        if (xwaylandModule is not null)
        {
            _xServer = _services.Require<Basin.XWayland.XWaylandServer>();
            _xwayland = new ShellXWayland(
                _shell,
                _layers,
                () => _workspaces?.ActiveTree ?? _layers.Workspaces,
                () => _shell.WorkArea?.Invoke(null) ?? PrimaryBox(),
                log)
            {
                Changed = () => _outputs.ScheduleAll(),
            };
            _xwayland.Changed = () =>
            {
                RefreshSurfaceLuts();
                _outputs.ScheduleAll();
            };
            xwaylandModule.WindowManagerReady += wm => _xwayland.Attach(wm);
            Console.WriteLine($"XWAYLAND {_xServer.DisplayName}");
        }

        _animations = new ShellAnimations(_layers, _ini)
        {
            Area = PrimaryBox,
            Changed = () => _outputs.ScheduleAll(),
        };
        _shell.Mapped = window => _animations.BeginMap(window);
        _shell.Unmapping = window => _animations.BeginUnmap(window);

        _deferredWorkspaces.Inner = _workspaces;
        _shell.WorkspaceTree = () => _workspaces.ActiveTree;
        _shell.WorkspaceTreeOf = window => _workspaces.TreeOf(window.Workspace);
        _shell.WorkspaceAdopt = window => _workspaces.Adopt(window);
        _switcher = new ShellSwitcher(_ui, _layers, _shell, _shellSurfaces)
        {
            Area = () => _layout.BoxOf(_outputs.Views[0].Output),
            Scale = () => _outputs.Views.FirstOrDefault()?.Output.Scale ?? 1.0,
            Changed = () => _outputs.ScheduleAll(),
        };

        _shell.Seat = Seat;
        _shell.Attach(Shell);
        _shell.Repaint = () => _outputs.ScheduleAll();
        _shell.PointerLocator = () => (_seat.PointerX, _seat.PointerY);
        _shell.OutputAt = (x, y) => _layout.OutputAt(x, y);
        _shell.OutputBoxOf = output => _layout.BoxOf(output);
        _shell.ScaleOf = output =>
            (output ?? _outputs.Views.FirstOrDefault()?.Output) is { } target ? target.Scale : 1.0;
        _shell.OutputPlacement = output => _layout.BoxOf(output);
        _shell.LockOutput = _outputs.Views.FirstOrDefault()?.Output;
        _shell.FrameFactory = window =>
        {
            if (!_decorate || Decorations.ModeOf(window.Window) != DecorationMode.ServerSide)
            {
                return null;
            }

            var frame = new ShellFrame(_ui, window.Tree, window, _shellSurfaces);
            frame.Update(window.Scale);
            return frame;
        };
        _shell.WorkArea = output =>
        {
            var target = output ?? _outputs.Views.FirstOrDefault()?.Output;
            if (target is null)
            {
                return new Box(0, 0, 1280, 720);
            }

            var box = _layout.BoxOf(target);
            return _avalonia.WorkArea(box.X, box.Y, box.Width, box.Height);
        };

        if (options.Backend == BackendKind.Drm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("no connected output");
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += Stop;
        }
    }

    internal Basin.Seat.Seat Seat { get; }

    internal XdgShell Shell { get; }

    internal XdgDecorationManager Decorations { get; }

    internal WestonIni Config => _ini;

    internal AvaloniaShell ShellUI => _avalonia;

    internal OutputDriver Outputs => _outputs;

    internal WestoniaSeat? SeatInput => _seat;

    internal Box PrimaryBox() => _outputs.Views.Count > 0
        ? _layout.BoxOf(_outputs.Views[0].Output)
        : new Box(0, 0, 1280, 720);

    internal void Stop() => _running = false;

    internal void WriteScreenshot() => WriteScreenshot(_options.Screenshot);

    internal void WriteScreenshot(string? screenshotPath)
    {
        if (screenshotPath is not { Length: > 0 } path ||
            _outputs.Views.FirstOrDefault() is not { } view)
        {
            return;
        }

        var mode = view.Output.CurrentMode;
        var target = new MemoryBuffer(mode.Width, mode.Height, DrmFormat.Xrgb8888);
        if (_scene.Render(_renderer, target, new RenderColor(0f, 0f, 0f, 1f), view.Output.Scale))
        {
            BufferCapture.WritePng(target, path);
            _log.LogInformation("screenshot written to {Path}", path);
        }

        target.Destroy();
    }

    private int RunLoop()
    {
        Console.WriteLine($"SOCKET {_host.Socket}");
        Console.Out.Flush();

        var interrupt = _host.Loop.AddSignal(Signal.Interrupt, _ => Stop());
        var terminate = _host.Loop.AddSignal(Signal.Terminate, _ => Stop());

        _seat?.CenterPointer();
        _uiDriver.Woken += _outputs.ScheduleAll;
        _uiDriver.Start();

        _clockTimer = _host.Loop.AddTimer(OnClockTick);
        OnClockTick();
        WireStdin();
        WireIdle();

        if (_ini.Shell.StartupAnimation != ShellAnimation.None)
        {
            _animations?.BeginSessionFade(toBlack: false);
        }

        if (_ini.Shell.Client is { Length: > 0 } client)
        {
            Spawn(client);
        }

        SpawnInputMethod();

        if (_ini.Autolaunch.Path is { Length: > 0 } autolaunch)
        {
            Spawn(autolaunch);
        }

        var frames = _options.Frames;
        while (_running && (frames == 0 || _outputs.PrimaryRendered < frames))
        {
            _uiDriver.Pump();
            _host.Loop.Dispatch(16);
            _host.Parent?.Flush();
        }

        _uiDriver.Woken -= _outputs.ScheduleAll;
        _stdinCommands?.Stop();
        _stdinCommands = null;

        _clockTimer?.Remove();
        interrupt.Remove();
        terminate.Remove();
        return 0;
    }

    private void SyncPopups() => _uiDriver.SyncPopups();

    private void OnOutputRemoved(OutputView view) => _avalonia.Remove(view.Output);

    private void OnOutputAdded(OutputView view)
    {
        var box = _layout.BoxOf(view.Output);
        _avalonia.Create(view.Output, box.Width, box.Height, view.Output.Scale);
        PlaceShell();
    }

    private void PlaceShell()
    {
        foreach (var view in _outputs.Views)
        {
            var box = _layout.BoxOf(view.Output);
            _avalonia.Place(view.Output, box.X, box.Y, box.Width, box.Height, view.Output.Scale);
        }

        _shell.RescaleFrames();
    }

    private void OnClockTick()
    {
        var format = _ini.Shell.ClockFormat;
        if (format == ClockFormat.None)
        {
            return;
        }

        var now = DateTime.Now;
        _avalonia.SetClock(format switch
        {
            ClockFormat.Seconds => now.ToString("h:mm:ss tt ddd MMM d"),
            ClockFormat.Minutes24H => now.ToString("HH:mm ddd MMM d"),
            ClockFormat.Seconds24H => now.ToString("HH:mm:ss ddd MMM d"),
            _ => now.ToString("h:mm tt ddd MMM d"),
        });

        var perSecond = format is ClockFormat.Seconds or ClockFormat.Seconds24H;
        var delay = perSecond
            ? 1000 - now.Millisecond
            : ((60 - now.Second) * 1000) - now.Millisecond;
        _clockTimer?.UpdateTimer(Math.Max(1, delay));
    }

    private void Spawn(string command)
    {
        try
        {
            var info = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(command);
            info.Environment["WAYLAND_DISPLAY"] = _host.Socket;
            if (_xServer?.DisplayName is { Length: > 0 } display)
            {
                info.Environment["DISPLAY"] = display;
            }

            var process = Process.Start(info);
            if (process is not null)
            {
                _spawned.Add(process);
            }
        }
        catch (Exception error)
        {
            _log.LogError("cannot start {Command}: {Reason}", command, error.Message);
        }
    }

    private static RenderStack CreateStack(string rendererName, ILogger log)
    {
        const string renderNode = "/dev/dri/renderD128";
        var name = rendererName;
        return RendererCatalog.CreateWithFallback(
            ref name,
            File.Exists(renderNode) ? renderNode : null,
            fallback => Report(log, fallback));
    }

    private static void Report(ILogger log, RendererFallback fallback)
    {
        if (fallback.Reason is null)
        {
            log.LogWarning(
                "{Renderer} requested but no render node was found; using software rendering", fallback.From);
            return;
        }

        log.LogWarning(
            "{Renderer} renderer unavailable ({Reason}); falling back to {Fallback}",
            fallback.From, fallback.Reason, fallback.To);
    }

    public void Dispose()
    {
        foreach (var process in _spawned)
        {
            process.Dispose();
        }

        _spawned.Clear();
        _xwayland?.Dispose();
        _animations?.Dispose();
        _idleTimer?.Remove();
        StopScreensaver();
        _lock?.Dispose();
        _workspaces?.Dispose();
        _switcher?.Dispose();
        _seat?.Dispose();
        _uiDriver.Dispose();
        _screens.Dispose();
        _cursor.Dispose();
        _input?.Dispose();
        _shell.Dispose();
        _avalonia.Dispose();
        _ui.Dispose();
        _outputs.Dispose();
        _scene.Root.Destroy();
        _services.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
