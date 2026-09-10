using System.Diagnostics;
using Basin;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;

namespace MauiComp;

internal sealed partial class MauiComp : IDisposable
{
    private readonly MauiCompOptions _options;
    private readonly BasinLogger _log;
    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private readonly BasinServices _services;
    private readonly ShellLayers _layers;
    private readonly OutputDriver _outputs;
    private readonly Basin.Color.ColorCapabilityPack _colorPack;
    private readonly CompositorRunLoop _loop;
    private readonly BasinGlGpu? _gpu;
    private readonly AvaloniaUIHost _ui;
    private readonly MauiShellLifetime _lifetime = new();
    private readonly MauiSurfaces _mauiSurfaces;
    private readonly ShellOutputs _shell;
    private readonly OutputScreens _screens;
    private readonly UISurfaceIndex _shellSurfaces = new();
    private readonly CursorController _cursor;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private readonly UIDriver _uiDriver;
    private readonly ShellAnimations _animations;
    private readonly List<Process> _spawned = [];
    private IEventSource? _clockTimer;
    private StdinCommands? _stdinCommands;

    public static int Run(MauiCompOptions options, BasinLogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var compositor = new MauiComp(options, log);
            status = compositor.RunLoop();
            rendered = compositor._outputs.PrimaryRendered;
            compositor.WriteScreenshot(options.Screenshot);
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

    internal MauiComp(MauiCompOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;

        var rendererName = options.Renderer;
        var stack = RendererCatalog.CreateWithFallback(
            ref rendererName,
            RendererCatalog.FindRenderNode(),
            fallback => log.Warn($"{fallback.Describe()}"));
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;
        RendererName = rendererName;

        _host = BasinHost.Create(
            HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()) with
            {
                SocketFd = options.SocketFd,
            });

        _colorPack = new Basin.Color.ColorCapabilityPack(_layout, _renderer);
        var servicePack = new DesktopServicePack(_scene, _layout, _renderer, _host.Drm);
        var capturePack = servicePack.Capture;
        _cursor = new CursorController(_layout) { Capture = capturePack.Capture };
        if (_host.Session is { } session && options.Backend == BackendKind.Drm)
        {
            _input = new Basin.Backend.Libinput.LibinputBackend(_host.Loop, session);
        }

        _services = _host.CreateServices()
            .Use(_layout)
            .With(servicePack)
            .With(_colorPack);
        _services.Install(DesktopPack.For("maui-comp"));
        if (_renderer.Device is { } renderDevice)
        {
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath));
        }

        _services.Freeze();

        _layers = new ShellLayers(_scene.Root);
        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = capturePack,
            Frames = _services.Require<IFrameClock>(),
            Requested = options.Outputs,
            Scales = options.Scales,
            ContinuousRepaint = options.Frames > 0,
            NestedName = index => $"maui-comp-{index + 1}",
            HeadlessMode = new OutputMode(1280, 720, 60_000),
        };
        _loop = new CompositorRunLoop(_host, _outputs);
        _outputs.Emptied += Stop;
        _outputs.ModesetRefused += card => log.Error($"modeset refused by {card.Name} in every mode");
        _outputs.Added += view =>
            BasinReport.Line($"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        _outputs.Added += OnOutputAdded;
        _outputs.Removed += OnOutputRemoved;
        _outputs.Added += view => _cursor.AddOutput(view.Output, view.Scene);
        _outputs.Removed += view => _cursor.RemoveOutput(view.Output);
        _outputs.LayoutChanged += PlaceShell;
        _animations = new ShellAnimations { Changed = () => _outputs.ScheduleAll() };
        _outputs.BeforeRepaint += view =>
        {
            var tick = new FrameTick(
                view.Scheduler?.PredictedVblankNanos ?? 0,
                view.Output.CurrentMode.RefreshIntervalNanoseconds is var interval and > 0 ? interval : 16_666_666L);
            _animations.Step(tick);
            if (_animations.IsRunning)
            {
                view.Scheduler?.ScheduleRepaint();
            }

            _uiDriver?.SyncPopups();
        };
        _screens = new OutputScreens(_outputs, _layout);

        _gpu = _renderer.Device is { } uiDevice
            ? BasinGlGpu.TryCreate(
                uiDevice.DevicePath, uiDevice as Basin.Render.Gl.GlDevice, _renderer.DmabufTextureFormats)
            : null;
        _ui = BasinPlatform.Start<ShellAvaloniaApp>(
            new BasinPlatformOptions
            {
                EventLoop = _host.Loop,
                Screens = _screens,
                Selection = _services.Find<ISelectionStore>(),
                Theme = options.Theme,
                Gpu = _gpu,
            },
            _lifetime);
        var app = (ShellAvaloniaApp)global::Avalonia.Application.Current!;
        _mauiSurfaces = new MauiSurfaces(app);
        _shell = new ShellOutputs(_ui, _mauiSurfaces, _layers, _shellSurfaces)
        {
            BackgroundImage = options.Background is { Length: > 0 } path && File.Exists(path) ? path : null,
        };
        _shell.PanelCreated += WirePanel;
        _uiDriver = new UIDriver(_ui, _host.Loop)
        {
            PopupLayer = _layers.Overlay,
            Index = _shellSurfaces,
        };

        Seat = _services.Require<Basin.Seat.Seat>();
        Shell = _services.Require<XdgShell>();
        Decorations = _services.Require<XdgDecorationManager>();
        Decorations.ModeChanged += (toplevel, mode) =>
            RecordDecorationPreference(toplevel.Surface, mode == DecorationMode.ServerSide);
        KdeDecorations = _services.Require<KdeServerDecorationManager>();
        KdeDecorations.ModeRequested += (surface, mode) =>
            RecordDecorationPreference(surface, mode == KdeServerDecorationManager.DecorationMode.Server);
        _services.Require<XdgToplevelSource>().NoBorderRequested += (toplevel, noBorder) =>
            RecordDecorationPreference(toplevel.Surface, !noBorder);

        _outputs.CreateInitialOutputs();
        if (_host.Parent is not null)
        {
            _cursor.UseParentCursor();
        }

        if (_services.Find<CursorShapeManager>() is { } shapes)
        {
            _cursor.Shapes = shapes;
            shapes.CursorRequested += _cursor.ShowImage;
        }

        Seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;
        _cursor.Load(new ShmAllocator(), 64, 64, 24);
        servicePack.CursorTheme.Images = _cursor.Images;
        ApplyKeymap();
        WireWindows();
        WireSeat();

        if (options.Backend == BackendKind.Drm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("no connected output");
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += Stop;
        }
    }

    internal string RendererName { get; }

    internal Basin.Seat.Seat Seat { get; }

    internal XdgShell Shell { get; }

    internal XdgDecorationManager Decorations { get; }

    internal KdeServerDecorationManager KdeDecorations { get; }

    internal Box PrimaryBox() => _outputs.Views.Count > 0
        ? _layout.BoxOf(_outputs.Views[0].Output)
        : new Box(0, 0, 1280, 720);

    internal Box WorkArea() => ShellOutputs.WorkArea(PrimaryBox());

    internal void Stop() => _loop.Stop();

    internal void ScheduleRepaint() => _outputs.ScheduleAll();

    internal void WriteScreenshot(string? screenshotPath)
    {
        if (screenshotPath is not { Length: > 0 } path || _outputs.Views.FirstOrDefault() is not { } view)
        {
            return;
        }

        if (SceneScreenshot.Write(_scene, _renderer, view.Output, path))
        {
            _log.Info($"screenshot written to {path}");
        }
    }

    private int RunLoop()
    {
        BasinReport.Line($"RENDERER {RendererName} chrome={_ui.Produces.ToString().ToLowerInvariant()}");
        BasinReport.Line(CompositorLines.Socket(_host.Socket));

        _seat?.CenterPointer();
        _uiDriver.Woken += _outputs.ScheduleAll;
        _uiDriver.Start();

        _clockTimer = _host.Loop.AddTimer(OnClockTick);
        OnClockTick();
        WireStdin();

        _loop.Iterating += _uiDriver.Pump;
        _loop.Frames = _options.Frames;
        _loop.Run();
        _loop.Iterating -= _uiDriver.Pump;

        _uiDriver.Woken -= _outputs.ScheduleAll;
        _stdinCommands?.Stop();
        _stdinCommands = null;
        _clockTimer?.Remove();
        return 0;
    }

    private void ApplyKeymap()
    {
        var names = Basin.Seat.SystemKeymap.Read();
        Seat.Keyboard.SetKeymap(names);
        Seat.Keyboard.SetRepeatInfo(25, 600);
        BasinReport.Line($"KEYMAP system layout={names.Layout ?? "default"} " + $"compiled={(Seat.Keyboard.Keymap is null ? "no" : "yes")}");
    }

    private void OnOutputAdded(OutputView view)
    {
        _shell.Create(view.Output, _layout.BoxOf(view.Output), view.Output.Scale);
        PlaceShell();
    }

    private void OnOutputRemoved(OutputView view) => _shell.Remove(view.Output);

    private void PlaceShell()
    {
        foreach (var view in _outputs.Views)
        {
            _shell.Place(view.Output, _layout.BoxOf(view.Output), view.Output.Scale);
        }

        RescaleTitlebars();
    }

    private void OnClockTick()
    {
        var now = DateTime.Now;
        _shell.SetClock(now.ToString("HH:mm ddd d MMM"));
        _clockTimer?.UpdateTimer(Math.Max(1, ((60 - now.Second) * 1000) - now.Millisecond));
    }

    private void Spawn(string command)
    {
        try
        {
            var process = BasinDiagnostics.StartClient(command, _host.Socket);
            if (process is not null)
            {
                _spawned.Add(process);
            }
        }
        catch (Exception error)
        {
            _log.Error($"cannot start {command}: {error.Message}");
        }
    }

    public void Dispose()
    {
        _colorPack.Luts.Dispose();
        foreach (var process in _spawned)
        {
            BasinDiagnostics.StopClient(process);
        }

        _spawned.Clear();
        _switcher?.Dispose();
        DisposeWindows();
        _animations.Dispose();
        _seat?.Dispose();
        _uiDriver.Dispose();
        _screens.Dispose();
        _cursor.Dispose();
        _input?.Dispose();
        _shell.Dispose();
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
