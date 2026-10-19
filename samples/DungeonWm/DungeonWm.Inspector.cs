using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Inspector;
using Basin.Inspector.Ipc;
using Basin.Ipc;
using Basin.Scene;
using Basin.UI.ImGui;
using Wayland;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private InspectorSession? _inspectorSession;
    private InspectorPhysicsPanel? _inspectorPhysics;
    private bool _inspectorFreeze;
    private string _modeReported = "window";
    private int _lastMoved;
    private int _lastImpacts;
    private long _impactTotal;

    private InspectorOverlay? _inspector => _inspectorSession?.Overlay;

    internal bool InspectorOpen => _inspectorSession is { IsOpen: true };

    internal OutputView? InspectorOutputView => _inspector?.Output;

    internal bool InspectorFreeze
    {
        get => _inspectorFreeze;
        set => _inspectorFreeze = value;
    }

    internal (int Moved, int Impacts, long ImpactTotal) StepCounts => (_lastMoved, _lastImpacts, _impactTotal);

    internal DungeonWmPhysics? PhysicsWorld => _physics;

    internal Basin.Scene3D.WorldCamera WorldCamera => _camera;

    internal DungeonWmWorld WorldModel => _world;

    internal MapRegion? ActiveRoom => _workspaces.Active;

    internal IReadOnlyDictionary<DungeonWmWindow, WindowSound> Sounds => _sounds;

    internal Basin.Scene3D.WorldVisibleSet? VisibleOn(OutputView view) =>
        _worldOutputs.TryGetValue(view, out var output) ? output.Visible : null;

    internal Basin.Scene3D.WorldView? ViewFor(OutputView view)
    {
        var slice = _layout.BoxOf(view.Output);
        return slice.IsEmpty || _layout.Bounds.IsEmpty ? null : _camera.ViewOf(_layout.Bounds, slice);
    }

    internal void RecordStep(int moved, int impacts)
    {
        _lastMoved = moved;
        _lastImpacts = impacts;
        _impactTotal += impacts;
    }

    internal void Report(string line) => BasinReport.Line(line);

    internal void Repaint() => ScheduleAll();

    private InspectorSession InspectorSession()
    {
        if (_inspectorSession is { } existing)
        {
            return existing;
        }

        var session = new InspectorSession(new InspectorSessionOptions
        {
            Driver = _outputs,
            Scene = _scene,
            Layer = _layers.Overlay,
            Loop = _host.Loop,
            Settle = _settle,
            Seat = _seat,
            Services = _services,
            Renderer = _renderer,
            Allocator = _deviceAllocator,
            Report = _report,
            DefaultOutput = () => CrosshairView() ?? OutputUnderPointer(),
            Settings = InspectorSettingsOf(_settings),
            AddPanels = AddInspectorPanels,
        });
        session.Changed += OnInspectorChanged;
        _touchChrome.First = session.Input;
        _inspectorSession = session;
        return session;
    }

    private static InspectorSettings InspectorSettingsOf(DungeonWmSettings settings) => new()
    {
        Enabled = settings.InspectorEnabled,
        Open = settings.InspectorOpen,
        Output = settings.InspectorOutput,
        Panels = settings.InspectorPanels,
        FontSize = settings.InspectorFontSize,
        RefreshHz = settings.InspectorRefreshHz,
    };

    private void AddInspectorPanels(InspectorOverlay overlay)
    {
        _inspectorPhysics = new InspectorPhysicsPanel(this) { RefreshHz = _settings.InspectorRefreshHz };
        overlay.Add(_inspectorPhysics);
        overlay.Add(new InspectorCameraPanel(this));
        overlay.Add(new InspectorRoomsPanel(this));
        overlay.Add(new InspectorAudioPanel(this));
        overlay.Add(new InspectorPosePanel(this));
    }

    private void ToggleInspector() => InspectorSession().Toggle();

    private void CloseInspector() => _inspectorSession?.Close();

    private void OnInspectorChanged()
    {
        var open = InspectorOpen;
        if (open != (_modeReported == "inspector"))
        {
            SyncMode();
            if (!open && !_crosshair)
            {
                PointerMoved((uint)Environment.TickCount);
            }
        }
    }

    private void StartInspector() => InspectorSession().Start();

    private void ApplyInspectorSettings()
    {
        if (_inspectorSession is not { } session)
        {
            return;
        }

        session.Apply(InspectorSettingsOf(_settings));
        if (_inspectorPhysics is { } physics)
        {
            physics.RefreshHz = _settings.InspectorRefreshHz;
        }
    }

    private bool RouteInspectorMotion(uint time)
    {
        if (_inspectorSession is not { } session || !session.Input.Motion(time, _pointer.X, _pointer.Y, out var cursor))
        {
            return false;
        }

        _cursor.SetHover(null, overClient: false);
        _cursor.ShowNamed(cursor ?? "default");
        return true;
    }

    private bool RouteInspectorButton(uint time, uint button, bool pressed) =>
        _inspectorSession is { } session && session.Input.Button(time, button, pressed);

    private bool RouteInspectorAxis(uint time, PointerAxis axis) =>
        _inspectorSession is { } session && session.Input.Axis(time, axis);

    private bool RouteInspectorKey(uint time, uint key, bool pressed) =>
        _inspectorSession is { } session && session.Input.Key(time, key, pressed);

    private void ReportModeChange()
    {
        var mode = ModeName;
        if (mode == _modeReported)
        {
            return;
        }

        _modeReported = mode;
        BasinReport.Line($"MODE {mode}");
        RaiseEvent("dungeonwm/mode");
    }

    private void RegisterInspectorCommands(IpcMethodRegistry methods)
    {
        InspectorSession().Register(methods);
        _report.Register(methods, "dungeonwm/inspector-pose", "inspector-pose", () =>
        {
            if (_inspector?.Panels.OfType<InspectorPosePanel>().FirstOrDefault() is { } pose)
            {
                pose.ReportTarget();
            }
            else
            {
                BasinReport.Line("POSE none");
            }
        });
    }

    private void DisposeInspector()
    {
        _inspectorSession?.Dispose();
        _inspectorSession = null;
    }
}
