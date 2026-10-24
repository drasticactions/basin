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
    private readonly ToplevelGeometryReporter? _geometryReporter;
    private readonly Basin.Desktop.ColorCapabilityPack _colorPack;
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
    private readonly Basin.Seat.Backends.HookInputSink _inputSink = new();
    private readonly Basin.Seat.Backends.InputConfigurator _inputConfigurator = new();
    private readonly UIDriver _uiDriver;
    private readonly ShellAnimations _animations;
    private readonly ProcessTracker _processes;
    private Basin.Host.ShellClock? _clock;

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
            log.Error(error, $"startup: the compositor did not start");
            return 1;
        }

        BasinReport.Line(CompositorLines.Frames(rendered));
        if (CompositorLines.Census() is { } census)
        {
            log.Error($"{census}");
        }

        return status;
    }

    internal MauiComp(MauiCompOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;
        _config = options.Config;

        var rendererName = options.Renderer;
        var stack = RendererCatalog.CreateWithFallback(
            ref rendererName,
            RendererCatalog.FindRenderNode(),
            fallback => fallback.Log(log));
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;
        RendererName = rendererName;

        _host = BasinHost.Create(
            HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()) with
            {
                SocketFd = options.SocketFd,
                Handoff = options.Handoff.Resolve(_config.Handoff.Mode),
            });
        _processes = new ProcessTracker(_host.Loop, () => ProcessTracker.SessionEnvironment(_host.Socket, null, _ipc?.Path));

        _colorPack = new Basin.Desktop.ColorCapabilityPack(_layout, _renderer);
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
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath, captureFormats: _renderer.DmabufRenderFormats));
        }

        _layers = new ShellLayers(_scene.Root);
        CreateLayerDrivers();
        _services.UseDefault<Basin.Capabilities.IInputDeviceConfiguration>(_inputConfigurator);
        _services.Use<Basin.Capabilities.IInputSink>(_inputSink);
        _services.Freeze();

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
        _geometryReporter = _services.Find<Basin.Capabilities.IToplevelModel>() is { } toplevelModel
            ? new ToplevelGeometryReporter(_outputs, _scene, toplevelModel)
            : null;
        _outputs.ConfigureHandoff(_config.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, _config.Handoff.DurationMs, _config.Handoff.HoldLimitMs);
        _loop = new CompositorRunLoop(_host, _outputs);
        _outputs.Emptied += Stop;
        _outputs.HandoffInherited += inheritance => BasinReport.Line(HandoffLines.Inherit(inheritance));
        _host.HandoffRetained += retention => BasinReport.Line(HandoffLines.Retain(retention));
        _outputs.ModesetRefused += card => log.Error($"output: {card.Name} refused a modeset in every mode");
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
            var clock = _outputs.AnimationClock;
            var predicted = view.Scheduler?.PredictedVblankNanos ?? 0;
            var tick = new FrameTick(
                clock?.FromMonotonic(predicted) ?? predicted,
                view.Output.CurrentMode.RefreshIntervalNanoseconds is var interval and > 0 ? interval : 16_666_666L);
            _animations.Step(tick);
            if (_animations.IsRunning && clock?.MarkAnimating() != false)
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
        _blur = Basin.Effects.BackdropBlurs.For(_renderer);
        if (_blur is not null)
        {
            _blur.Options = _blur.Options with { Strength = _config.BlurStrength };
        }

        _shell.PanelBackdrop = () => BlurWhen(_config.BlurTaskbar);
        _uiDriver = new UIDriver(_ui, _host.Loop)
        {
            PopupLayer = _layers.Overlay,
            Index = _shellSurfaces,
        };

        Seat = _services.Require<Basin.Seat.Seat>();
        Shell = _services.Require<XdgShell>();
        Decorations = _services.Require<XdgDecorationManager>();
        KdeDecorations = _services.Require<KdeServerDecorationManager>();
        _decorationPreferences = new DecorationPreferences(
            Decorations, KdeDecorations, _services.Require<XdgToplevelSource>());
        _decorationPreferences.Changed += OnDecorationChanged;

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
        WireLayers();
        WireColor();
        WireSeat();
        WireInspector();

        if (options.Backend == BackendKind.Drm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("No output is connected. Connect a display, then start the compositor again.");
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += Stop;
        }

        if (_seat is { } seat)
        {
            seat.Binder.UseKeyboardAids(_host.Loop);
            _access = new Basin.Accessibility.DesktopAccessibility(new Basin.Accessibility.DesktopAccessibilityOptions
            {
                Name = "maui-comp",
                Host = _host,
                Scene = _scene,
                Seat = Seat,
                Pointer = seat.Binder.Pointer,
                Processes = _processes,
                TextInput = _services.Find<Basin.Desktop.TextInputManager>(),
                Toplevels = _services.Find<Basin.Capabilities.IToplevelModel>(),
                Aids = seat.Binder.Aids,
                StartToolkit = _ui.StartAccessibilityAsync,
                Log = _log,
            });
            _access.Apply(_config.Accessibility);
            _cursor.Curtain = _access.Curtain;
            _ = _access.StartAsync(_config.Accessibility.Enabled);
        }
    }

    private Basin.Accessibility.DesktopAccessibility? _access;

    internal string RendererName { get; }

    internal Basin.Seat.Seat Seat { get; }

    internal XdgShell Shell { get; }

    internal XdgDecorationManager Decorations { get; }

    internal KdeServerDecorationManager KdeDecorations { get; }

    internal Box PrimaryBox() => _outputs.Views.Count > 0
        ? _layout.BoxOf(_outputs.Views[0].Output)
        : new Box(0, 0, 1280, 720);

    internal Box WorkArea() => _outputs.Views.Count > 0
        ? WorkAreaOf(_outputs.Views[0].Output, PrimaryBox())
        : ShellOutputs.WorkArea(PrimaryBox());

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
            _log.Info($"screenshot: written to {path}");
        }
    }

    private int RunLoop()
    {
        var hangup = _host.Loop.AddSignal(Signal.Hangup, _ => Reload());
        BasinReport.Line($"RENDERER {RendererName} chrome={_ui.Produces.ToString().ToLowerInvariant()}");
        BasinReport.Line(CompositorLines.Socket(_host.Socket));
        Basin.Cli.CurrentDesktop.Export("basin", _host.Drm is null ? null : _host.Socket);

        _seat?.CenterPointer();
        _uiDriver.Woken += _outputs.ScheduleAll;
        _uiDriver.Start();

        _clock = new Basin.Host.ShellClock(_host.Loop, now => now.ToString("HH:mm ddd d MMM"), text => _shell.SetClock(text!));
        _clock.Refresh();
        WireIpc();
        _inspector?.Start();

        _loop.Iterating += _uiDriver.Pump;
        _loop.Frames = _options.Frames;
        _loop.Run();
        _loop.Iterating -= _uiDriver.Pump;
        hangup.Remove();

        _uiDriver.Woken -= _outputs.ScheduleAll;
        _ipc?.Dispose();
        _settle?.Dispose();
        _ipc = null;
        _clock?.Dispose();
        return _loop.ExitCode;
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

    private void OnOutputRemoved(OutputView view)
    {
        _usable.Remove(view.Output);
        _shell.Remove(view.Output);
    }

    private void PlaceShell()
    {
        foreach (var view in _outputs.Views)
        {
            _shell.Place(view.Output, _layout.BoxOf(view.Output), view.Output.Scale);
        }

        RelayoutLayers();
        RescaleTitlebars();
    }

    private void Spawn(string command)
    {
        if (_processes.SpawnShell(command, out var error) is null)
        {
            _log.Warn($"launch: cannot start {command}, {CliMessage.Text(error ?? string.Empty)}");
        }
    }

    public void Dispose()
    {
        _host.DisconnectClients();
        _ipc?.Dispose();
        _settle?.Dispose();
        _ipc = null;
        _access?.Dispose();
        _access = null;
        _colorPack.Luts.Dispose();
        _processes.TerminateAll(TimeSpan.FromMilliseconds(500));
        _processes.Dispose();
        _switcher?.Dispose();
        _inspector?.Dispose();
        DisposeWindows();
        _animations.Dispose();
        _inputConfigurator.Dispose();
        _seat?.Dispose();
        _uiDriver.Dispose();
        _screens.Dispose();
        _cursor.Dispose();
        _input?.Dispose();
        _shell.Dispose();
        _ui.Dispose();
        _gpu?.Dispose();
        _geometryReporter?.Dispose();
        _outputs.Dispose();
        _scene.Root.Destroy();
        _blur?.Dispose();
        _services.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
