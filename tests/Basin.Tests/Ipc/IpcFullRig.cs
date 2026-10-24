using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Ipc;
using Basin.Scene;

namespace Basin.Tests;

internal static class IpcFullRig
{
    public static IpcTestRig Create(
        string? path = null, bool listen = false, Action<IpcServer>? register = null, TestSelectionStore? selection = null,
        Action<BasinServices>? services = null)
    {
        selection ??= new TestSelectionStore();
        var extra = services;
        var rig = new IpcTestRig(
            (services, host) =>
            {
                extra?.Invoke(services);
                services.UseDefault<IX11DensityControl>(new NextStartDensityControl());
                services.Use(host.Layout);
                services.Use<IToplevelModel>(new TestToplevelModel());
                services.Use<IToplevelStack>(new TestToplevelStack());
                services.Use<IWorkspaceModel>(new TestWorkspaceModel());
                services.Use<IOutputConfiguration>(new LayoutOutputConfiguration(host.Layout));
                services.Use<IOutputPower>(new TestOutputPower());
                services.Use<IIdleSource>(new IpcReadEventTests.TestIdle());
                services.Use<ILockState>(new Lock());
                services.Use<IActiveKeymap>(new Keymap());
                services.Use<IKeymapLookup>(new IpcInputTests.TableLookup());
                services.Use<IInputSink>(new RecordingInputSink());
                services.Use<IScreenCapture>(new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer });
                services.Use<ISelectionStore>(selection);
                var devices = new FakeInputDeviceConfiguration();
                devices.AddTouchpad();
                services.Use<IInputDeviceConfiguration>(devices);
                services.UseDefault<ISceneDiagnostics>(new QuietDiagnostics());
                services.UseDefault<IUIElementLocator>(new Basin.Capabilities.Defaults.UIElementDirectory());
                services.UseDefault<IInputDiagnostics>(new QuietInputDiagnostics());
                services.UseDefault<ISurfaceDiagnostics>(new SurfaceDiagnostics(host.Compositor));
                services.UseDefault<IExplicitSyncDiagnostics>(new QuietSyncDiagnostics());
                var clock = new DefaultAnimationClock();
                services.UseDefault<IAnimationClock>(clock);
                services.UseDefault<IAnimationClockControl>(clock);
            },
            new IpcSessionInfo { Compositor = "full", Quit = () => { }, Settle = new ManualSettle() },
            path,
            listen);
        _ = rig.Own(selection);
        rig.Server.SyntheticInput = new IpcInputTests.RecordingSynthetic();
        rig.Server.Approvals = new IpcApprovalBroker();
        register?.Invoke(rig.Server);
        return rig;
    }

    internal sealed class QuietSyncDiagnostics : IExplicitSyncDiagnostics
    {
        public ExplicitSyncCounters Counters => default;
    }

    internal sealed class QuietInputDiagnostics : IInputDiagnostics
    {
        public List<(bool Motion, Action<InputTraceEvent> Sink)> Watchers { get; } = [];

        public IDisposable WatchInput(bool motion, Action<InputTraceEvent> sink)
        {
            var entry = (motion, sink);
            Watchers.Add(entry);
            return new Undo(() => Watchers.Remove(entry));
        }

        private sealed class Undo(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }
    }

    internal sealed class QuietDiagnostics : ISceneDiagnostics
    {
        public List<(bool Stacks, Action<DamageInfo> Sink)> DamageWatchers { get; } = [];

        public List<Action<FrameInfo>> FrameWatchers { get; } = [];

        public RecordRequest? LastRecording { get; private set; }

        public Action<RecordingResult>? PendingRecording { get; private set; }

        public bool DeferRecordings { get; set; }

        public int ClockSinks => 0;

        public IReadOnlyList<SceneNodeInfo> Tree() =>
            [new SceneNodeInfo(0, "tree", null, "tree", 0, 0, 0, 0, true, string.Empty),
             new SceneNodeInfo(1, "rect", "probe", "tree/probe", 4, 5, 10, 6, false, " size=10x6")];

        public IReadOnlyList<FrameInfo> Frames() =>
            [new FrameInfo("probe", null, 3, false, true, new Box(-6, -36, 712, 529), true, false, false,
                FramePart.Close, FramePart.None, FramePart.None, true, 10, 2, 1)];

        public IDisposable WatchDamage(bool stacks, Action<DamageInfo> sink)
        {
            var entry = (stacks, sink);
            DamageWatchers.Add(entry);
            return new Undo(() => DamageWatchers.Remove(entry));
        }

        public IDisposable WatchFrames(Action<FrameInfo> sink)
        {
            FrameWatchers.Add(sink);
            return new Undo(() => FrameWatchers.Remove(sink));
        }

        private sealed class Undo(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }

        public Func<Box?>? LocateWindow(Surface surface, int margin) => () => new Box(0, 0, 100, 80);

        public Func<Box?>? LocateSurface(Surface surface, int margin) => () => new Box(5, 6, 30, 20);

        public Func<Box?>? LocateNode(string path, int margin, out string? error)
        {
            error = path == "tree/probe" ? null : $"no node at '{path}'";
            return error is null ? () => new Box(4, 5, 10, 6) : null;
        }

        public string? Record(RecordRequest request, Action<RecordingResult> done)
        {
            LastRecording = request;
            if (DeferRecordings)
            {
                PendingRecording = done;
                return null;
            }

            done(Result(request));
            return null;
        }

        public static RecordingResult Result(RecordRequest request) => new(
            request.Path, 2, 40, 30, 40, 60, 10, 12, 100, 140, 7, false,
            [new RecordFrameStats(0, new Box(1, 2, 40, 30), 10, 100, 0, [], false),
             new RecordFrameStats(1, new Box(3, 2, 40, 30), 12, 140, 7, [new Box(0, 0, 40, 30)], false)])
        {
            Png = request.InlinePng ? Basin.Diagnostics.PngCodec.Encode(new byte[2 * 2 * 4], 2, 2) : null,
        };

        public bool WriteParts(Surface surface, Action<FramePart, Box> part)
        {
            part(FramePart.Title, new Box(10, 0, 100, 30));
            part(FramePart.Close, new Box(90, 5, 20, 20));
            return true;
        }

        public bool? PlanesAllowed { get; private set; }

        public void AllowPlanes(bool allowed) => PlanesAllowed = allowed;

        public IReadOnlyList<PlaneReport> Planes() =>
            [new PlaneReport("HEADLESS-1", null, [new PlaneDecision("tree/glass", new Box(0, 0, 40, 30), false, "blend-space")])];

        public IDisposable WatchPlanes(Action<PlaneReport> sink) => new Undo(() => { });
    }

    private sealed class NextStartDensityControl : IX11DensityControl
    {
        public bool TrySet(ulong toplevelId, X11Density density, Action<X11DensityResult> completed, out string? error)
        {
            error = null;
            completed(new X11DensityResult(X11DensityOutcome.AppliesNextStart, "exec=test x11=native"));
            return true;
        }
    }

    private sealed class Lock : ILockState
    {
        public bool IsLocked => false;

        public void AddObserver(ILockStateObserver observer)
        {
        }

        public void RemoveObserver(ILockStateObserver observer)
        {
        }
    }

    private sealed class Keymap : IActiveKeymap
    {
        public (int Fd, uint Size)? KeymapBuffer => null;

        public event Action? KeymapChanged
        {
            add { }
            remove { }
        }
    }
}
