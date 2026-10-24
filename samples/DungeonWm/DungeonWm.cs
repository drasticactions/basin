using Basin;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Render.Gl;
using Basin.Render.Vulkan;
using Basin.Render3D.Gl;
using Basin.Render3D.Vulkan;
using Basin.Renderers;
using Basin.Scene;
using Basin.Scene3D;
using Basin.Scene3D.Scene;
using Basin.Shell.Xdg;

namespace DungeonWm;

internal sealed partial class DungeonWm : IDisposable
{
    private readonly DungeonWmOptions _options;
    private readonly BasinLogger _log;
    private readonly DungeonWmSettings _settings;
    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly IWorldRenderer _worldRenderer;
    private readonly BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private readonly SceneLayers _layers;
    private readonly SceneTree _worldLayer;
    private readonly BasinServices _services;
    private readonly OutputDriver _outputs;
    private readonly ProcessTracker _processes;
    private readonly CompositorRunLoop _loop;
    private readonly SceneCapturePack _capturePack;
    private readonly Basin.Desktop.ColorCapabilityPack _colorPack;
    private readonly CursorController _cursor;
    private readonly Basin.Backend.Libinput.LibinputBackend? _input;
    private readonly Basin.Seat.Backends.InputConfigurator _inputConfigurator = new();
    private readonly Basin.Seat.Seat _seat;
    private readonly XdgShell _shell;
    private readonly IToplevelModel _toplevels;
    private readonly SurfacePresenceTracker _presence;
    private readonly FractionalScaleManager? _fractionalScale;
    private readonly Dictionary<OutputView, WorldSceneOutput> _worldOutputs = [];
    private readonly List<DungeonWmWindow> _windows = [];
    private readonly Basin.Desktop.NestedPopupPlacer _popups;
    private readonly List<SurfaceBox> _presenceScratch = [];
    private readonly DungeonWmWorld _world;
    private readonly DungeonWmFrames _frames;
    private readonly DungeonWmPlaques _plaques;
    private readonly DungeonWmWorkspaces _workspaces;
    private readonly WorldCamera _camera = new();
    private readonly Walker _walker;
    private readonly WorldPicker _picker;
    private readonly LayerShellSceneDriver _layerDriver;
    private readonly SessionLockSceneDriver _lockDriver;
    private readonly Basin.XWayland.XWaylandServer? _xServer;
    private readonly Basin.XWayland.XWaylandModule? _xwaylandModule;
    private DungeonWmWindow? _focused;
    private DungeonWmWindow? _takeover;
    private DungeonWmWindow? _pendingTakeover;
    private (System.Numerics.Vector3 Position, float Yaw, float Pitch)? _beforeTakeover;
    private bool _locked;
    private long _lastStep;
    private readonly WorldFrameCallbacks _frameCallbacks = new();

    public static int Run(DungeonWmOptions options, BasinLogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var dungeonWm = new DungeonWm(options, log);
            status = dungeonWm.RunLoop();
            rendered = dungeonWm._outputs.PrimaryRendered;
            dungeonWm.WriteScreenshot(options.Screenshot);
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

    private DungeonWm(DungeonWmOptions options, BasinLogger log)
    {
        _options = options;
        _log = log;
        _settings = options.Settings;

        var stack = CreateStack(options.Renderer, log, out var rendererName);
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;
        _worldRenderer = WorldRenderers.For(_renderer, out var worldDeclined)
            ?? throw new InvalidOperationException($"The {rendererName} renderer cannot draw the world: {worldDeclined}");
        RendererName = rendererName;

        _host = BasinHost.Create(HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()) with
        {
            SocketFd = options.SocketFd,
            Handoff = options.Handoff.Resolve(_settings.Handoff.Mode),
        });
        _processes = new ProcessTracker(_host.Loop, LaunchEnvironment)
        {
            X11Routes = density => _xwaylandModule?.Route(density),
            X11Default = launch => _settings.Launch.ForArgv(launch.DesktopId, launch.Argv),
        };

        var kit = Kit.Load();
        _popups = new Basin.Desktop.NestedPopupPlacer(_layout);
        _world = new DungeonWmWorld(kit, _settings);
        _frames = new DungeonWmFrames(kit.Images.Count > 0 ? kit.Images[0] : null, _settings.Frames, _settings.Map.Seed);
        for (var i = 0; i < Math.Max(1, _settings.Rooms); i++)
        {
            _world.AddRoom();
        }

        _workspaces = new DungeonWmWorkspaces(_world);
        _walker = new Walker(_camera, _settings);
        _picker = new WorldPicker(_world.World);
        _camera.FieldOfView = _settings.FieldOfViewRadians;
        _camera.Near = _settings.Near;
        _camera.Far = _settings.Far;
        var first = _world.Map.Rooms[0];
        _walker.Teleport(_world.CenterOf(first, _settings.EyeHeight), 0);
        _workspaces.SetActive(first);

        _colorPack = new Basin.Desktop.ColorCapabilityPack(_layout, _renderer);
        var servicePack = new DesktopServicePack(_scene, _layout, _renderer, _host.Drm);
        _capturePack = servicePack.Capture;
        _capturePack.Capture.Background = new RenderColor(_settings.SkyColor.X, _settings.SkyColor.Y, _settings.SkyColor.Z, 1);
        _cursor = new CursorController(_layout) { Capture = _capturePack.Capture };
        if (_host.Session is { } session && options.Backend == BackendKind.Drm)
        {
            _input = new Basin.Backend.Libinput.LibinputBackend(_host.Loop, session);
        }

        _services = _host.CreateServices()
            .Use(_layout)
            .With(servicePack)
            .With(_colorPack)
            .Use<IWorkspaceModel>(_workspaces);
        _services.Install(DesktopPack.For("dungeon-wm"));
        if (_renderer.Device is { } renderDevice)
        {
            _services.Install(new LinuxDmabufModule(
                _renderer.DmabufTextureFormats, renderDevice.DevicePath, captureFormats: _renderer.DmabufRenderFormats));
        }

        Basin.XWayland.XWaylandModule? xwaylandModule = null;
        if (options.XWayland)
        {
            xwaylandModule = new Basin.XWayland.XWaylandModule { IncludeLowDensity = _settings.XWaylandLowDensity };
            _xwaylandModule = xwaylandModule;
            _services.Install(xwaylandModule);
        }

        _services.UseDefault<IInputDeviceConfiguration>(_inputConfigurator);
        _services.Use<IInputSink>(_inputSink);
        _layers = new SceneLayers(_scene.Root);
        _layerDriver = new LayerShellSceneDriver(_services, _layout, _layers);
        _lockDriver = new SessionLockSceneDriver(_services, _layers.Lock, _layout, SetLocked);
        _services.Freeze();

        _plaques = new DungeonWmPlaques(_renderer, _deviceAllocator, _scene.Root, log);
        _worldLayer = new SceneTree(_layers.Background);
        _worldLayer.LowerToBottom();
        _layers.Windows.Enabled = false;

        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = _capturePack,
            Frames = _services.Require<IFrameClock>(),
            Background = new RenderColor(_settings.SkyColor.X, _settings.SkyColor.Y, _settings.SkyColor.Z, 1),
            Requested = options.Outputs,
            Scales = options.Scales,
            ContinuousRepaint = options.Frames > 0,
            HeadlessMode = new OutputMode(options.Width, options.Height, 60_000),
            NestedName = index => options.Outputs == 1 ? "dungeon-wm" : $"dungeon-wm-{index + 1}",
        };
        _outputs.ConfigureHandoff(_settings.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, _settings.Handoff.DurationMs, _settings.Handoff.HoldLimitMs);
        _loop = new CompositorRunLoop(_host, _outputs) { Outro = new WindowsFadeOutro(_layers, TimeSpan.FromMilliseconds(300)) };
        _outputs.Emptied += Stop;
        _outputs.HandoffInherited += inheritance => BasinReport.Line(HandoffLines.Inherit(inheritance));
        _host.HandoffRetained += retention => BasinReport.Line(HandoffLines.Retain(retention));
        _outputs.ModesetRefused += card => log.Error($"output: {card.Name} refused a modeset in every mode");

        _seat = _services.Require<Basin.Seat.Seat>();
        _shell = _services.Require<XdgShell>();
        _toplevels = _services.Require<IToplevelModel>();
        _fractionalScale = _services.Find<FractionalScaleManager>();
        _presence = new SurfacePresenceTracker(_layout, (surface, scale) => _fractionalScale?.AnnounceScale(surface, scale));

        _outputs.Added += OnOutputAdded;
        _outputs.Removed += OnOutputRemoved;
        _outputs.BeforeRepaint += OnBeforeRepaint;
        _outputs.Painted += OnPainted;
        _outputs.LayoutChanged += () =>
        {
            _layerDriver.Rearrange();
            _lockDriver.Reconfigure();
            Relock();
        };

        _capturePack.Attach(_toplevels, surface =>
            WindowOf(surface) is { } window ? new ToplevelCaptureTrees(window.Scene.Tree, null, window.Scene.Tree) : null);

        _shell.NewToplevel += OnNewToplevel;
        _popups.ConstrainBox = toplevel =>
            _takeover is { } taken && ReferenceEquals(taken.Surface, toplevel.Surface) &&
            _layout.OutputAt(taken.Scene.Tree.X + taken.Geometry.X, taken.Scene.Tree.Y + taken.Geometry.Y) is { } output
                ? _layout.BoxOf(output)
                : null;
        _popups.Changed += ScheduleAll;
        _shell.NewPopup += OnNewPopup;

        _layerDriver.Accept = _ => _outputs.Views.Count > 0;
        _layerDriver.DefaultOutput = _ => _outputs.Views[0].Global;
        _layerDriver.TrackPopups(_shell);
        TrackLayerKeyboard(_layerDriver);

        _lockDriver.Locked += () => BasinReport.Line($"LOCKED");
        _lockDriver.Unlocked += () => BasinReport.Line($"UNLOCKED");

        _workspaces.Windows = () => _windows;
        _workspaces.ActivateRequested = FlyToRoom;
        _workspaces.CreateRequested = () =>
        {
            var room = _world.AddRoom();
            ReplaceHungWindows();
            _workspaces.Added(room);
            BasinReport.Line($"ROOM + {room.Workspace + 1}");
            ScheduleAll();
        };
        _workspaces.RemoveRequested = room =>
        {
            room.Removed = true;
            _workspaces.Removed(room);
            BasinReport.Line($"ROOM - {room.Workspace + 1}");
        };
        _workspaces.AssignRequested = (window, room) => Hang(window, room, null);
        _workspaces.Outputs = outputs =>
        {
            var count = Math.Min(outputs.Length, _outputs.Views.Count);
            for (var i = 0; i < count; i++)
            {
                outputs[i] = _outputs.Views[i].Output;
            }

            return count;
        };

        _world.Rebuilt += ScheduleAll;
        _world.Rebuilt += RebuildDoorways;
        RebuildDoorways();

        WireColor();
        _outputs.CreateInitialOutputs();
        if (options.Backend == BackendKind.Drm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("No output is connected. Connect a display, then start the compositor again.");
        }

        if (_host.Parent is { } parent)
        {
            _cursor.UseParentCursor();
            parent.ParentGone += Stop;
        }

        if (_services.Find<CursorShapeManager>() is { } shapes)
        {
            _cursor.Shapes = shapes;
            shapes.CursorRequested += _cursor.ShowImage;
        }

        _seat.Pointer.CursorRequested += _cursor.HandleCursorRequest;
        _seat.Keyboard.SetKeymap(Basin.Seat.SystemKeymap.Read());
        WireInput();
        servicePack.CursorTheme.Images = _cursor.Images;
        StartPhysics();
        StartAvatar();
        StartAudio();
        StartSounds();

        if (xwaylandModule is not null)
        {
            _xServer = xwaylandModule.Native!.Server;
            BasinReport.Line($"XWAYLAND {_xServer.DisplayName}");
            if (xwaylandModule.LowDensity is { } lowDensity)
            {
                BasinReport.Line($"XWAYLAND LOW {lowDensity.DisplayName}");
            }

            foreach (var display in xwaylandModule.Displays)
            {
                display.WindowManagerReady += AttachXWayland;
            }
        }
    }

    public string RendererName { get; }

    public DungeonWmWorld WorldState => _world;

    public WorldCamera Camera => _camera;

    public IReadOnlyList<DungeonWmWindow> Windows => _windows;

    public void Stop() => _loop.Stop();

    public void Dispose()
    {
        _disposing = true;
        StopAudio();
        StopSounds();
        _host.DisconnectClients();
        DropSnapshots();
        _colorPack.Luts.Dispose();
        foreach (var output in _worldOutputs.Values)
        {
            output.Dispose();
        }

        _worldOutputs.Clear();
        DisposeInspector();
        _outputs.Dispose();
        _plaques.Dispose();
        _scene.Root.Destroy();
        _worldRenderer.Dispose();
        _physics?.Dispose();
        _services.Dispose();
        _cursor.Dispose();
        _input?.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }

    private int RunLoop()
    {
        BasinReport.Line(CompositorLines.Socket(_host.Socket));
        BasinReport.Line($"WORLD renderer={RendererName} rooms={_world.Map.Rooms.Count} seed={_settings.Map.Seed}");
        using var ipc = StartIpc();
        StartInspector();
        using var settle = _settle;
        using var processes = _processes;
        InitProcess? startup = null;
        if (_options.Command is { } command)
        {
            var initEnvironment = InitLaunchTag(out var tag);
            foreach (var (name, value) in _xwaylandModule?.ChildEnvironment() ?? new Dictionary<string, string>())
            {
                initEnvironment[name] = value;
            }

            startup = InitProcess.Start(
                command, _host.Socket, _xServer?.DisplayName, _log, initEnvironment,
                _settings.Launch.ForCommand(null, command) is { } density ? _xwaylandModule?.Route(density) : null);
            if (startup is not null)
            {
                _launchGroups[tag] = startup.ProcessGroup;
            }
        }

        var hangup = _host.Loop.AddSignal(Signal.Hangup, _ => Reload());
        _loop.Frames = _options.Frames;
        _loop.Run();
        hangup.Remove();
        startup?.Stop(_log);
        ipc?.Processes.TerminateAll(TimeSpan.FromMilliseconds(500));
        return 0;
    }

    private static RenderStack CreateStack(string requested, BasinLogger log, out string name)
    {
        if (requested is not ("vulkan" or "gl"))
        {
            throw new InvalidOperationException(
                $"The {requested} renderer cannot draw the world. Use the vulkan or gl renderer.");
        }

        var node = Basin.Renderers.RendererCatalog.FindRenderNode() ?? throw new InvalidOperationException("No GPU render node is available. The world needs a GPU renderer.");
        name = requested;
        return Basin.Renderers.RendererCatalog.CreateWithFallback(
            ref name, node, fallback => fallback.Log(log), floor: "gl");
    }

    private void SetLocked(bool locked)
    {
        _locked = locked;
        _layers.SetLocked(locked);
        ApplyLayerState();
        SyncMode();
        ScheduleAll();
    }

    private void ApplyLayerState()
    {
        var takeover = _takeover is not null;
        _layers.Windows.Enabled = takeover && !_locked;
        _worldLayer.Enabled = !takeover;
    }

    private void ScheduleAll() => _outputs.ScheduleAll();

    private void OnOutputAdded(OutputView view)
    {
        var allocator = _deviceAllocator ?? view.Allocator ?? throw new InvalidOperationException("No allocator is available for the world buffers.");
        _worldOutputs[view] = new WorldSceneOutput(_worldLayer, allocator);
        _presence.AddOutput(view.Output, view.Global);
        _cursor.AddOutput(view.Output, view.Scene);
        _layerDriver.Rearrange();
        BasinReport.Line($"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        SyncMode();
    }

    private void OnOutputRemoved(OutputView view)
    {
        if (_worldOutputs.Remove(view, out var output))
        {
            output.Dispose();
        }

        _presence.RemoveOutput(view.Output);
        _cursor.RemoveOutput(view.Output);
        ForgetCrosshairOutput(view);
    }
}
