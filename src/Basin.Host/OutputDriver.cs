using Basin.Backend.Drm;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Wayland.Server;

namespace Basin.Host;

public sealed class OutputDriver : IDisposable, Scene.IPresentedOutputs
{
    private readonly BasinHost _host;
    private readonly Scene.Scene _scene;
    private readonly OutputLayout _layout;
    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly List<OutputView> _views = [];
    private IFrameClock? _frames;
    private readonly List<IAllocator> _ownedAllocators = [];
    private readonly OutputState _frameState = new();
    private readonly Dictionary<OutputView, Action<SceneNode?, Box>> _secondaryDamage = [];
    private readonly Dictionary<OutputView, IDisposable> _hostChrome = [];
    private OutputView? _preparing;

    private const OutputStateFields FrameFields =
        OutputStateFields.Buffer | OutputStateFields.Damage | OutputStateFields.InFence |
        OutputStateFields.OutFence | OutputStateFields.Layers | OutputStateFields.Tearing;
    private readonly List<DrmBackend> _secondaryCards = [];
    private bool? _hasLidSwitch;
    private bool _shmAdvertised;
    private bool _initialCreated;

    private bool AllRendered()
    {
        foreach (var view in _views)
        {
            if (view.Rendered == 0 && !view.IsSecondary)
            {
                return false;
            }
        }

        return _views.Count > 0;
    }
    private bool _hdrMetadataPassthrough;
    private OutputContentType? _contentTypeOverride;

    public OutputDriver(
        BasinHost host, Scene.Scene scene, OutputLayout layout, IRenderer renderer, IAllocator? deviceAllocator)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(renderer);

        _host = host;
        _scene = scene;
        _layout = layout;
        _renderer = renderer;
        _deviceAllocator = deviceAllocator;
        host.AttachDriver(this);
    }

    public Scene.Scene Scene => _scene;

    public IRenderer Renderer => _renderer;

    public SceneCapturePack? Capture { get; set; }

    public IFrameClock? Frames
    {
        get => _frames;
        set
        {
            if (ReferenceEquals(_frames, value))
            {
                return;
            }

            if (_frames is { } previous)
            {
                previous.FrameRequested -= OnFrameRequested;
            }

            _frames = value;
            if (value is not null)
            {
                value.FrameRequested += OnFrameRequested;
            }
        }
    }

    public RenderColor Background { get; set; } = new(0f, 0f, 0f, 1f);

    public bool AllowDirectScanout { get; set; } = true;

    public BasinHost Host => _host;

    public OutputContentType? ContentTypeOverride
    {
        get => _contentTypeOverride;
        set
        {
            _contentTypeOverride = value;
            foreach (var view in _views)
            {
                if (view.Scene is { } scene)
                {
                    scene.ContentTypeOverride = value;
                }
            }
        }
    }

    public Func<IOutput, OutputTransform?>? ConfiguredTransform { get; set; }

    public bool HdrMetadataPassthrough
    {
        get => _hdrMetadataPassthrough;
        set
        {
            _hdrMetadataPassthrough = value;
            foreach (var view in _views)
            {
                if (view.Scene is { } scene)
                {
                    scene.HdrMetadataPassthrough = value;
                }
            }
        }
    }

    public bool AllowPlaneOffload { get; set; } = true;

    public VrrPacingPolicy VrrPacing { get; set; } = VrrPacingPolicy.Fullscreen;

    public bool AllowTearing { get; set; } = true;

    public Capabilities.ISurfaceHints? SurfaceHints { get; set; }

    public int Requested { get; set; } = 1;

    public double[] Scales { get; set; } = [];

    public Func<IOutput, double?>? ConfiguredScale { get; set; }

    public bool HasLidSwitch
    {
        get => _hasLidSwitch ??= ProbeLidSwitch();
        set => _hasLidSwitch = value;
    }

    public OutputMode HeadlessMode { get; set; } = new(1280, 720, 60_000);

    public Func<int, string?> NestedName { get; set; } = index => $"basin-{index + 1}";

    public bool ContinuousRepaint { get; set; }

    public bool LastOnly { get; set; }

    public bool FullRepaint { get; set; }

    public bool DecorateNested { get; set; } = true;

    public Func<WaylandOutput, WaylandHostFrame, IDisposable?>? HostChrome { get; set; }

    public bool DebugDamageTint { get; set; }

    public Action<IReadOnlyList<OutputView>>? Arrange { get; set; }

    public IReadOnlyList<OutputView> Views => _views;

    public OutputView? ViewAt(double x, double y)
    {
        if (_layout.OutputAt(x, y) is not { } output)
        {
            return null;
        }

        foreach (var view in _views)
        {
            if (ReferenceEquals(view.Output, output))
            {
                return view;
            }
        }

        return null;
    }

    public long PrimaryRendered
    {
        get
        {
            foreach (var view in _views)
            {
                if (!view.IsSecondary)
                {
                    return view.Rendered;
                }
            }

            return 0;
        }
    }

    public event Action<OutputView>? Added;

    public event Action<OutputView>? Removed;

    public event Action? Emptied;

    public event Action<OutputView>? BeforeRepaint;

    public event Action<OutputView>? Painted;

    private Action<SceneOutput>? _presented;

    IReadOnlyList<SceneOutput> Scene.IPresentedOutputs.Outputs
    {
        get
        {
            var outputs = new List<SceneOutput>(_views.Count);
            foreach (var view in _views)
            {
                if (view.Scene is { } scene)
                {
                    outputs.Add(scene);
                }
            }

            return outputs;
        }
    }

    event Action<SceneOutput>? Scene.IPresentedOutputs.Presented
    {
        add => _presented += value;
        remove => _presented -= value;
    }

    bool Scene.IPresentedOutputs.CapturePresented { get; set; }

    bool Scene.IPresentedOutputs.TryReadPresented(
        SceneOutput output, IRenderer renderer, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? rgba, out int width, out int height)
    {
        rgba = null;
        width = 0;
        height = 0;
        foreach (var view in _views)
        {
            if (view.Scene == output && view.LastPresentedBuffer is { } buffer && Diagnostics.BufferCapture.TryReadRgba(buffer, renderer, out rgba))
            {
                width = buffer.Width;
                height = buffer.Height;
                return true;
            }
        }

        return false;
    }

    void Scene.IPresentedOutputs.RequestRepaint(SceneOutput output)
    {
        foreach (var view in _views)
        {
            if (view.Scene == output)
            {
                output.Ring.AddWhole();
                view.Scheduler?.ScheduleRepaint();
            }
        }
    }

    void Scene.IPresentedOutputs.AllowPlanes(bool allowed)
    {
        AllowPlaneOffload = allowed;
        AllowDirectScanout = allowed;
    }

    private void RaisePainted(OutputView view)
    {
        Painted?.Invoke(view);
        if (_presented is { } presented && view.Scene is { } scene)
        {
            presented(scene);
        }
    }

    public event Action<OutputView>? ModeChanged;

    public event Action<OutputView, OutputTransform>? TransformChanged;

    public event Action<OutputView, OutputState>? StampFrame;

    public event Action<DrmOutput, OutputState>? StampModeset;

    public event Action? LayoutChanged;

    public event Action<OutputView, ScanoutChoice>? ScanoutChanged;

    public event Action<OutputView>? HostScaleFollowed;

    public event Action<OutputView, double>? ScaleRefused;

    public event Action<DrmOutput>? ModesetRefused;

    public event Action<HandoffInheritance>? HandoffInherited;

    public event Action<OutputView, string>? HandoffSkipped;

    public event Action<OutputView>? HandoffStarted;

    public event Action<OutputView>? HandoffEnded;

    public IHandoffTransition? Handoff { get; set; } = new CrossfadeTransition(TimeSpan.FromMilliseconds(400));

    public HandoffHold HandoffHold { get; set; }

    public TimeSpan HandoffHoldLimit { get; set; } = TimeSpan.FromSeconds(3);

    public SceneTree? HandoffLayer { get; set; }

    public Func<long> HandoffClock { get; set; } = static () => MonotonicClock.Nanos;

    private bool _handoffReleased;
    private IEventSource? _handoffLimit;

    private RetainRun? _retain;

    public bool Retaining => _retain is { Finished: false };

    public void RetainAndStop(IHandoffOutro? outro, TimeSpan budget, Action done)
    {
        ArgumentNullException.ThrowIfNull(done);
        if (_retain is not null)
        {
            return;
        }

        var run = new RetainRun(outro, done);
        _retain = run;
        run.Deadline = _host.Loop.AddTimer(() => FinishRetain(run, timedOut: true));
        run.Deadline.UpdateTimer((int)Math.Clamp(budget.TotalMilliseconds, 1, int.MaxValue));
        foreach (var view in _views)
        {
            if (view.IsSecondary)
            {
                continue;
            }

            outro?.Begin(view);
            var watched = view;
            Action handler = () => OnRetainFlip(run, watched);
            view.Output.Frame += handler;
            run.FrameHandlers.Add((view.Output, handler));
        }

        if (outro is null)
        {
            run.OutroDone = true;
        }

        if (PrimaryViews() == 0)
        {
            FinishRetain(run, timedOut: false);
            return;
        }

        ScheduleAll();
    }

    private int PrimaryViews()
    {
        var count = 0;
        foreach (var view in _views)
        {
            if (!view.IsSecondary)
            {
                count++;
            }
        }

        return count;
    }

    private void StepRetain(OutputView view)
    {
        if (_retain is not { Finished: false } run || view.IsSecondary)
        {
            return;
        }

        if (!run.OutroDone && run.Outro is { } outro)
        {
            var now = HandoffClock();
            if (run.OutroStarted < 0)
            {
                if (!outro.Ready)
                {
                    view.Scheduler?.ScheduleRepaint();
                    return;
                }

                run.OutroStarted = now;
            }

            var duration = (long)outro.Duration.TotalNanoseconds;
            var progress = duration <= 0 ? 1.0 : Math.Min(1.0, (double)(now - run.OutroStarted) / duration);
            outro.Step(view, progress);
            if (progress < 1.0)
            {
                view.Scheduler?.ScheduleRepaint();
                return;
            }

            run.OutroDone = true;
            foreach (var other in _views)
            {
                if (!other.IsSecondary)
                {
                    outro.End(other);
                }
            }
        }

        if (run.Flattening.Add(view))
        {
            view.Flatten = true;
            if (view.Output is IHardwareCursor cursor)
            {
                _ = cursor.SetCursor(null, 0, 0);
            }
        }
    }

    private void OnRetainFlip(RetainRun run, OutputView view)
    {
        if (run.Finished || !run.AwaitingFlip.Remove(view))
        {
            return;
        }

        run.Presented.Add(view);
        if (run.Presented.Count >= PrimaryViews())
        {
            FinishRetain(run, timedOut: false);
        }
    }

    private void FinishRetain(RetainRun run, bool timedOut)
    {
        if (run.Finished)
        {
            return;
        }

        run.Finished = true;
        run.Deadline?.Remove();
        run.Deadline = null;
        foreach (var (output, handler) in run.FrameHandlers)
        {
            output.Frame -= handler;
        }

        run.FrameHandlers.Clear();
        if (timedOut)
        {
            HostLog.Log.Info($"the exit sequence ran out of time with {run.Presented.Count} of {PrimaryViews()} outputs flattened; retaining what is lit");
        }

        if (_host.Drm is { } drm)
        {
            drm.ExitMode = DrmExitMode.Retain;
        }

        run.Done();
    }

    public void ConfigureHandoff(bool crossfade, int durationMs, int holdLimitMs)
    {
        Handoff = crossfade ? new CrossfadeTransition(TimeSpan.FromMilliseconds(Math.Max(0, durationMs))) : null;
        HandoffHoldLimit = TimeSpan.FromMilliseconds(Math.Max(0, holdLimitMs));
    }

    public void ReleaseHandoff()
    {
        _handoffReleased = true;
        KickHandoffs();
    }

    public bool StartHandoff(OutputView view, IBuffer frame, int sourceWidth, int sourceHeight, IDisposable? owner = null) =>
        StartHandoff(view, frame, sourceWidth, sourceHeight, null, owner);

    private bool StartHandoff(OutputView view, IBuffer frame, int sourceWidth, int sourceHeight, BufferBase? copy, IDisposable? owner)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(frame);
        if (Handoff is not { } transition || view.Handoff is not null || view.Box.Width <= 0 || view.Box.Height <= 0)
        {
            copy?.Destroy();
            owner?.Dispose();
            return false;
        }

        SceneTree? ownedLayer = null;
        var layer = HandoffLayer ?? (ownedLayer = new SceneTree(_scene.Root) { ExcludeFromScanout = true });
        var data = new HandoffFrame(
            view.Output.Name,
            view.Box.Width,
            view.Box.Height,
            sourceWidth > 0 ? sourceWidth : frame.Width,
            sourceHeight > 0 ? sourceHeight : frame.Height);
        view.Handoff = new HandoffRun(
            data,
            transition,
            layer,
            ownedLayer,
            view.Box,
            frame,
            data.FrameWidth,
            data.FrameHeight,
            copy,
            owner,
            HandoffClock());
        _handoffLimit ??= _host.Loop.AddTimer(KickHandoffs);
        _handoffLimit.UpdateTimer((int)Math.Clamp(HandoffHoldLimit.TotalMilliseconds, 1, int.MaxValue));
        HostLog.Log.Info($"{view.Output.Name}: handoff from a {data.FrameWidth}x{data.FrameHeight} {frame.GetType().Name} over {transition.Duration.TotalMilliseconds:F0} ms");
        HandoffStarted?.Invoke(view);
        view.Scheduler?.ScheduleRepaint();
        return true;
    }

    private void KickHandoffs()
    {
        foreach (var view in _views)
        {
            if (view.Handoff is not null)
            {
                view.Scheduler?.ScheduleRepaint();
            }
        }
    }

    private void StepHandoff(OutputView view)
    {
        if (view.Handoff is not { } run)
        {
            return;
        }

        var now = HandoffClock();
        if (run.Started < 0)
        {
            var released = HandoffHold == HandoffHold.Ready ? _host.Ready : _handoffReleased;
            if (!released && now - run.Created < (long)HandoffHoldLimit.TotalNanoseconds)
            {
                return;
            }

            run.Started = now;
        }

        var duration = (long)run.Transition.Duration.TotalNanoseconds;
        var progress = duration <= 0 ? 1.0 : Math.Min(1.0, (double)(now - run.Started) / duration);
        if (progress < 1.0)
        {
            run.Transition.Step(run.Frame, progress);
            run.Apply();
            view.Scheduler?.ScheduleRepaint();
            return;
        }

        AllocationScope.Pause();
        try
        {
            EndHandoff(view);
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private void EndHandoff(OutputView view)
    {
        if (view.Handoff is not { } run)
        {
            return;
        }

        run.End();
        view.Handoff = null;
        run.Dispose();
        HandoffEnded?.Invoke(view);
        view.Scheduler?.ScheduleRepaint();
    }

    private void BeginInherited(OutputView view, InheritedFrame inherited)
    {
        if (_renderer.DmabufTextureFormats.Contains(inherited.Format, inherited.Modifier))
        {
            _ = StartHandoff(view, inherited.Dmabuf, inherited.SourceWidth, inherited.SourceHeight, null, inherited);
            return;
        }

        if (inherited.CpuReadable && inherited.CopyToMemory() is { } copy)
        {
            inherited.Dispose();
            _ = StartHandoff(view, copy, copy.Width, copy.Height, copy, null);
            return;
        }

        var reason = inherited.CpuReadable ? "copy-failed" : $"format {inherited.Format} modifier 0x{inherited.Modifier:x}";
        HostLog.Log.Info($"{view.Output.Name}: no handoff transition ({reason}); the first frame is a cut");
        HandoffSkipped?.Invoke(view, reason);
        inherited.Dispose();
    }

    public event Action<DrmDeviceInfo>? CardAdopted;

    public event Action<DrmDeviceInfo, string>? CardRefused;

    public event Action<DrmOutput>? SecondaryOutputAdded;

    public void AdoptSecondaryCards()
    {
        if (_host.Drm is not { } primary || _host.Session is not { } session)
        {
            return;
        }

        foreach (var device in DrmDevices.Enumerate())
        {
            if (device.HasConnectors && device.CardPath != primary.DevicePath)
            {
                AdoptSecondaryCard(session, device);
            }
        }
    }

    private void AdoptSecondaryCard(Basin.Session.ISession session, DrmDeviceInfo device)
    {
        DrmBackend? backend = null;
        try
        {
            backend = new DrmBackend(_host.Loop, session, device.CardPath);
            backend.Start();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            backend?.Dispose();
            CardRefused?.Invoke(device, e.Message);
            return;
        }

        _secondaryCards.Add(backend);
        var allocator = new DumbAllocator(backend);
        _ownedAllocators.Add(allocator);
        CardAdopted?.Invoke(device);
        foreach (var output in backend.Outputs)
        {
            AddSecondaryOutput(output, allocator);
        }

        backend.OutputAdded += output => AddSecondaryOutput(output, allocator);
        backend.OutputRemoved += output =>
        {
            if (ViewOf(output) is { } view)
            {
                RemoveView(view);
            }
        };
    }

    private void AddSecondaryOutput(DrmOutput output, IAllocator allocator)
    {
        if (!EnableWithStamp(output))
        {
            ModesetRefused?.Invoke(output);
            return;
        }

        SecondaryOutputAdded?.Invoke(output);
        _ = AddView(output, allocator, secondary: true);
    }

    public void CreateInitialOutputs()
    {
        if (_host.Drm is { } drm)
        {
            foreach (var card in drm.Outputs)
            {
                OnNewDrmOutput(card);
            }

            drm.OutputAdded += OnNewDrmOutput;
            drm.OutputRemoved += card =>
            {
                if (ViewOf(card) is { } view)
                {
                    RemoveView(view);
                }
            };

            _initialCreated = true;
            return;
        }

        for (var i = 0; i < Math.Max(1, Requested); i++)
        {
            if (_host.Parent is { } parent)
            {
                AddView(parent.CreateOutput(NestedName(i)));
            }
            else
            {
                AddView(_host.Headless!.CreateOutput(HeadlessMode));
            }
        }

        _initialCreated = true;
    }

    public OutputView? ViewOf(IOutput output)
    {
        foreach (var view in _views)
        {
            if (ReferenceEquals(view.Output, output))
            {
                return view;
            }
        }

        return null;
    }

    public OutputView AddView(IOutput output) => AddView(output, null, secondary: false);

    public OutputView AddView(IOutput output, IAllocator? allocator, bool secondary = false)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (LastOnly && _views.Count >= 1)
        {
            Disable(_views[^1]);
        }

        if (!_shmAdvertised && _host.Services?.Find<ShmGlobal>() is { } shm)
        {
            shm.Advertise(_renderer.ShmTextureFormats);
            _shmAdvertised = true;
        }

        var view = new OutputView(output, new OutputGlobal(_host.Display, output))
        {
            IsSecondary = secondary,
        };
        var index = _views.Count;
        _views.Add(view);

        var configured = Scales.Length == 0 ? ConfiguredScale?.Invoke(output) : null;
        var scale = ChooseScale(output, index, configured);
        if (scale != output.Scale)
        {
            using var state = new OutputState();
            if (!output.Commit(state.SetScale(scale)))
            {
                ScaleRefused?.Invoke(view, scale);
            }
        }

        var transform = ConfiguredTransform?.Invoke(output) ??
            (output.PanelOrientation != OutputTransform.Normal ? output.PanelOrientation : output.Transform);
        if (transform != output.Transform)
        {
            using var state = new OutputState();
            _ = output.Commit(state.SetTransform(transform));
        }

        view.Scale = output.Scale;
        view.Transform = output.Transform;
        _layout.Add(output, 0, 0);
        Relayout();
        if (allocator is not null)
        {
            view.Allocator = allocator;
            view.SwapModifiers = [DrmFormatSet.ModifierLinear];
        }
        else
        {
            ChooseScanout(view);
        }

        WireView(view);
        if (Scales.Length == 0 && configured is null && output is WaylandOutput hosted)
        {
            hosted.HostScaleChanged += () => FollowHostScale(view, hosted);
        }

        Added?.Invoke(view);
        if (FullRepaint)
        {
            OracleRepaint(view);
        }

        return view;
    }

    private static bool ProbeLidSwitch()
    {
        try
        {
            foreach (var device in Directory.EnumerateDirectories("/sys/class/input"))
            {
                var path = Path.Combine(device, "capabilities", "sw");
                if (!File.Exists(path))
                {
                    continue;
                }

                var words = File.ReadAllText(path).Split(
                    ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (words.Length > 0 &&
                    ulong.TryParse(words[^1], System.Globalization.NumberStyles.HexNumber, null, out var bits) &&
                    (bits & 1) != 0)
                {
                    return true;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }

        return false;
    }

    private double ChooseScale(IOutput output, int index, double? configured)
    {
        if (Scales.Length > 0)
        {
            return Scales[Math.Min(index, Scales.Length - 1)];
        }

        if (configured is { } pinned)
        {
            return pinned;
        }

        if (output is DrmOutput card)
        {
            var outputClass = card.Class == OutputClass.Handheld && HasLidSwitch
                ? OutputClass.Laptop
                : card.Class;
            return OutputScale.Choose(card.CurrentMode, card.PhysicalSize, outputClass);
        }

        return 1;
    }

    public void SetScale(OutputView view, double scale)
    {
        ArgumentNullException.ThrowIfNull(view);

        using var state = new OutputState();
        if (!view.Output.Commit(state.SetScale(scale)))
        {
            ScaleRefused?.Invoke(view, scale);
            return;
        }

        view.Scale = view.Output.Scale;
        Relayout();
        ModeChanged?.Invoke(view);
        view.Scheduler?.ScheduleRepaint();
    }

    private void FollowHostScale(OutputView view, WaylandOutput hosted)
    {
        if (Math.Abs(hosted.HostScale - hosted.Scale) < 0.0001)
        {
            return;
        }

        using var hostState = new OutputState();
        if (!hosted.Commit(hostState.SetScale(hosted.HostScale)))
        {
            return;
        }

        view.Scale = hosted.Scale;
        Relayout();
        HostScaleFollowed?.Invoke(view);
    }

    public void RemoveView(OutputView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var nested = view.Output is WaylandOutput;
        _views.Remove(view);
        Removed?.Invoke(view);
        _layout.Remove(view.Output);
        Capture?.DmabufCapture.Forget(view.Output);
        Teardown(view, retire: true);

        if (_views.Count == 0 && nested)
        {
            Emptied?.Invoke();
            return;
        }

        if (LastOnly && _views.Count > 0)
        {
            Enable(_views[^1]);
        }

        Relayout();
    }

    public void RepaintNow(OutputView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (FullRepaint)
        {
            OracleRepaint(view);
            return;
        }

        view.Scene?.Ring.AddWhole();
        Repaint(view);
    }

    public void ScheduleAll()
    {
        foreach (var view in _views)
        {
            view.Scheduler?.ScheduleRepaint();
        }
    }

    public void Relayout()
    {
        if (Arrange is { } arrange)
        {
            arrange(_views);
        }
        else
        {
            _layout.ArrangeHorizontally(_views.Select(v => v.Output));
        }

        foreach (var view in _views)
        {
            view.Box = _layout.BoxOf(view.Output);
        }

        LayoutChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_retain is { Finished: false } retain)
        {
            retain.Finished = true;
            retain.Deadline?.Remove();
            foreach (var (output, handler) in retain.FrameHandlers)
            {
                output.Frame -= handler;
            }
        }

        _handoffLimit?.Remove();
        _handoffLimit = null;
        foreach (var view in _views)
        {
            Teardown(view, retire: false);
        }

        _views.Clear();
        foreach (var allocator in _ownedAllocators)
        {
            allocator.Dispose();
        }

        _ownedAllocators.Clear();
        foreach (var card in _secondaryCards)
        {
            card.Dispose();
        }

        _secondaryCards.Clear();
        _frameState.Dispose();
    }

    public static bool EnableWithMode(DrmOutput card) => EnableWithMode(card, null);

    public bool EnableWithStamp(DrmOutput card) => EnableWithMode(card, StampModeset);

    private static bool EnableWithMode(DrmOutput card, Action<DrmOutput, OutputState>? stamp)
    {
        ArgumentNullException.ThrowIfNull(card);

        using var state = new OutputState();
        var start = card.StartMode;
        state.SetEnabled(true).SetMode(start);
        stamp?.Invoke(card, state);
        if (card.TestCommit(state))
        {
            return card.Commit(state);
        }

        foreach (var mode in card.Modes)
        {
            if (mode == start)
            {
                continue;
            }

            state.Clear();
            state.SetEnabled(true).SetMode(mode);
            stamp?.Invoke(card, state);
            if (card.TestCommit(state))
            {
                return card.Commit(state);
            }
        }

        return false;
    }

    private void OnNewDrmOutput(DrmOutput card)
    {
        card.HandoffInherited += (_, inheritance) => HandoffInherited?.Invoke(inheritance);
        InheritedFrame? inherited = null;
        var refusal = string.Empty;
        var wanted = Handoff is not null && _host.Handoff.Inherits && card.Inherited is not null;
        if (wanted && !card.TryImportInherited(out inherited, out refusal))
        {
            inherited = null;
        }

        if (!EnableWithMode(card, StampModeset))
        {
            inherited?.Dispose();
            ModesetRefused?.Invoke(card);
            return;
        }

        var view = AddView(card);
        if (inherited is not null)
        {
            BeginInherited(view, inherited);
        }
        else if (wanted)
        {
            HostLog.Log.Info($"{view.Output.Name}: no handoff transition ({refusal}); the first frame is a cut");
            HandoffSkipped?.Invoke(view, refusal);
        }
    }

    private void AttachHostChrome(OutputView view, WaylandOutput nested, WaylandHostFrame frame)
    {
        if (_hostChrome.Remove(view, out var previous))
        {
            previous.Dispose();
        }

        var chrome = HostChrome is { } custom
            ? custom(nested, frame)
            : DecorateNested ? Basin.Backend.Wayland.Skia.DefaultHostChrome.Attach(nested, frame) : null;
        if (chrome is not null)
        {
            _hostChrome[view] = chrome;
        }
    }

    private void Teardown(OutputView view, bool retire)
    {
        if (view.Handoff is { } handoff)
        {
            view.Handoff = null;
            handoff.Dispose();
        }

        if (_hostChrome.Remove(view, out var chrome))
        {
            chrome.Dispose();
        }

        if (_secondaryDamage.Remove(view, out var damage))
        {
            _scene.Damaged -= damage;
        }

        view.Scheduler?.Dispose();
        RenderFences.CloseFence(view.RenderFence);
        view.RenderFence = -1;
        view.Scene?.Dispose();
        _host.PresentedSets.Retire(view.Output);
        view.Swapchain?.Dispose();
        if (retire)
        {
            view.Global.Retire();
        }
        else
        {
            view.Global.Dispose();
        }
    }

    private void Enable(OutputView view)
    {
        using var state = new OutputState();
        if (!view.Output.Commit(state.SetEnabled(true)))
        {
            return;
        }

        if (!_layout.Contains(view.Output))
        {
            _layout.Add(view.Output, 0, 0);
        }

        Relayout();
        view.Scheduler?.ScheduleRepaint();
    }

    private void Disable(OutputView view)
    {
        if (!view.Output.Enabled)
        {
            return;
        }

        using var state = new OutputState();
        view.Output.Commit(state.SetEnabled(false));
        _layout.Remove(view.Output);
    }

    private void ChooseScanout(OutputView view)
    {
        if (view.Output is not DrmOutput card || _host.Drm is null)
        {
            if (_deviceAllocator is { } nestedDevice && _host.Parent is { } parent)
            {
                var common = SwapchainFormats.CommonModifiers(
                    nestedDevice, parent.ParentDmabufFormats, DrmFormat.Xrgb8888);
                if (common.Length > 0)
                {
                    view.Allocator = nestedDevice;
                    view.SwapModifiers = common;
                    ScanoutChanged?.Invoke(view, ScanoutChoice.DeviceBuffers);
                    return;
                }
            }

            view.Allocator = SharedShmAllocator();
            view.SwapModifiers = [DrmFormatSet.ModifierLinear];
            return;
        }

        if (_deviceAllocator is { } device)
        {
            var shared = device.Formats.Intersect(card.ScanoutFormats)
                .ModifiersOf(DrmFormat.Xrgb8888).ToArray();
            if (shared.Length > 0 && device.CanScanOut(card, shared, DrmFormat.Xrgb8888))
            {
                view.Allocator = device;
                view.SwapModifiers = shared;
                ScanoutChanged?.Invoke(view, ScanoutChoice.DeviceBuffers);
                return;
            }
        }

        var dumb = new DumbAllocator(_host.Drm);
        _ownedAllocators.Add(dumb);
        view.Allocator = dumb;
        view.SwapModifiers = [DrmFormatSet.ModifierLinear];
        ScanoutChanged?.Invoke(view, ScanoutChoice.DumbLinear);
    }

    private IAllocator SharedShmAllocator()
    {
        if (_ownedAllocators.Count == 0 || _ownedAllocators[0] is not ShmAllocator)
        {
            var shm = new ShmAllocator();
            _ownedAllocators.Insert(0, shm);
            return shm;
        }

        return _ownedAllocators[0];
    }

    private void WireView(OutputView view)
    {
        if (view.Output is WaylandOutput nested)
        {
            nested.CloseRequested += () => RemoveView(view);
            nested.HostFrameAvailable += frame => AttachHostChrome(view, nested, frame);
        }

        if (FullRepaint)
        {
            view.Output.Frame += () => OracleRepaint(view);
            return;
        }

        var scheduler = new OutputScheduler(_host.Loop, view.Output);
        view.Scheduler = scheduler;
        scheduler.Repaint += () => Repaint(view);
        view.Output.Frame += () => SendFrameDoneAtFlip(view);
        view.Output.Committed += fields =>
        {
            if ((fields & ~FrameFields) != 0)
            {
                scheduler.ScheduleRepaint();
            }
        };
        if (view.Output is IPresentingOutput presenting)
        {
            presenting.PresentedOnScreen += (timeNs, _, _) => scheduler.NotifyPresented((long)timeNs);
        }

        if (view.IsSecondary)
        {
            void OnDamage(SceneNode? _, Box box)
            {
                var watched = view.ReplicaSource is { } replicaSource ? replicaSource.Box : view.Box;
                if (!ReferenceEquals(_preparing, view) &&
                    !watched.IsEmpty && box.X < watched.X + watched.Width && box.X + box.Width > watched.X &&
                    box.Y < watched.Y + watched.Height && box.Y + box.Height > watched.Y)
                {
                    scheduler.ScheduleRepaint();
                }
            }

            _secondaryDamage[view] = OnDamage;
            _scene.Damaged += OnDamage;
            scheduler.ScheduleRepaint();
            return;
        }

        var sceneOutput = new SceneOutput(_scene, view.Output)
        {
            Presented = _host.PresentedSets.For(view.Output),
            HdrMetadataPassthrough = _hdrMetadataPassthrough,
            ContentTypeOverride = _contentTypeOverride,
            SurfaceHints = Hints(),
        };
        view.Scene = sceneOutput;
        sceneOutput.ScanoutCandidateChanged += surface => view.ScanoutCandidate = surface;
        view.Output.Frame += () => ReportRenderTime(view);
        sceneOutput.DamagePending += () =>
        {
            if (!ReferenceEquals(_preparing, view))
            {
                scheduler.ScheduleRepaint();
            }
        };
        Capture?.DmabufCapture.Track(view.Output, sceneOutput);
        scheduler.ScheduleRepaint();
    }

    private bool SyncViewGeometry(OutputView view, out OutputMode mode)
    {
        mode = view.Output.CurrentMode;
        if (mode.Width <= 0 || mode.Height <= 0)
        {
            return false;
        }

        var resized = mode.Width != view.Width || mode.Height != view.Height || view.Scale != view.Output.Scale;
        var rotated = view.Transform != view.Output.Transform;
        if (resized || rotated)
        {
            AllocationScope.Pause();
            try
            {
                (view.Width, view.Height) = (mode.Width, mode.Height);
                view.Scale = view.Output.Scale;
                var was = view.Transform;
                view.Transform = view.Output.Transform;
                Relayout();
                if (resized)
                {
                    ModeChanged?.Invoke(view);
                }

                if (rotated)
                {
                    TransformChanged?.Invoke(view, was);
                }
            }
            finally
            {
                AllocationScope.Resume();
            }
        }

        return true;
    }

    private void EnsureSwapchain(OutputView view, in OutputMode mode)
    {
        if (view.Swapchain is null)
        {
            AllocationScope.Pause();
            view.Swapchain = new Swapchain(
                view.Allocator!, mode.Width, mode.Height, DrmFormat.Xrgb8888, view.SwapModifiers);
            AllocationScope.Resume();
        }
        else if (mode.Width != view.Swapchain.Width || mode.Height != view.Swapchain.Height)
        {
            view.Swapchain.Resize(mode.Width, mode.Height);
        }
    }

    private int _scopedRepaints;

    private void OnFrameRequested()
    {
        foreach (var view in _views)
        {
            view.Scheduler?.ScheduleRepaint();
        }
    }

    private void Repaint(OutputView view)
    {
        if (!view.Output.Enabled)
        {
            return;
        }

        if (_scopedRepaints < 30)
        {
            _scopedRepaints++;
            RepaintCore(view);
            return;
        }

        AllocationScope.Begin(region: "Repaint", forgiving: true);
        try
        {
            RepaintCore(view);
        }
        finally
        {
            AllocationScope.End();
        }
    }

    private void RepaintCore(OutputView view)
    {

        AllocationScope.Pause();
        try
        {
            Frames?.BeginFrame(view.Output, view.Scheduler?.PredictedVblankNanos ?? 0);
        }
        finally
        {
            AllocationScope.Resume();
        }

        if (!SyncViewGeometry(view, out var mode))
        {
            return;
        }

        StepHandoff(view);
        AllocationScope.Pause();
        try
        {
            StepRetain(view);
        }
        finally
        {
            AllocationScope.Resume();
        }

        AllocationScope.Pause();
        _preparing = view;
        try
        {
            BeforeRepaint?.Invoke(view);
        }
        finally
        {
            _preparing = null;
            AllocationScope.Resume();
        }

        if (view.IsSecondary)
        {
            SecondaryRepaint(view, mode);
            return;
        }

        EnsureSwapchain(view, mode);

        if (ContinuousRepaint || view.Flatten)
        {
            view.Scene!.Ring.AddWhole();
        }

        if (view.ReplicaSource is { } replicaSource && _layout.Contains(replicaSource.Output))
        {
            view.Scene!.ReplicationSource = _layout.BoxOf(replicaSource.Output);
        }
        else
        {
            view.Scene!.ReplicationSource = null;
            view.Scene.Position = new Point(view.Box.X, view.Box.Y);
        }

        _frameState.Clear();
        StampPacing(view);
        AllocationScope.Pause();
        try
        {
            StampFrame?.Invoke(view, _frameState);
        }
        finally
        {
            AllocationScope.Resume();
        }

        var renderStart = MonotonicClock.Nanos;
        var composedBefore = view.Scene.ComposedCommits;
        var committed = view.Scene.Commit(
            _renderer, view.Swapchain!, _frameState, new SceneCommitOptions
            {
                Background = Background,
                DebugDamageTint = DebugDamageTint,
                AllowDirectScanout = AllowDirectScanout && !view.Flatten,
                AllowPlaneOffload = AllowPlaneOffload && !view.Flatten,
                TargetPresentNanos = Math.Max(view.Scheduler!.PredictedVblankNanos, MonotonicClock.Nanos),
            });
        if (committed)
        {
            view.Scheduler!.NotifyCommitted();
            view.LastPresentedBuffer = _frameState.Buffer;
            view.Rendered++;
            if (view.Flatten && _retain is { Finished: false } retain)
            {
                view.Flatten = false;
                AllocationScope.Pause();
                try
                {
                    retain.AwaitingFlip.Add(view);
                }
                finally
                {
                    AllocationScope.Resume();
                }
            }

            if (_initialCreated && !_host.Ready && AllRendered())
            {
                AllocationScope.Pause();
                try
                {
                    _host.NotifyReady();
                    KickHandoffs();
                }
                finally
                {
                    AllocationScope.Resume();
                }
            }
            if (view.Scene.ComposedCommits != composedBefore)
            {
                TrackRenderFence(view, renderStart);
            }
        }
        else if (view.Scene.NeedsRepaint)
        {
            if (view.Rendered == 0 && _host.Drm is { } drm && view.Allocator is not DumbAllocator)
            {
                AllocationScope.Pause();
                try
                {
                    ScanoutChanged?.Invoke(view, ScanoutChoice.RefusedByPlane);
                    view.Swapchain!.Dispose();
                    view.Swapchain = null;
                    var dumb = new DumbAllocator(drm);
                    _ownedAllocators.Add(dumb);
                    view.Allocator = dumb;
                    view.SwapModifiers = [DrmFormatSet.ModifierLinear];
                }
                finally
                {
                    AllocationScope.Resume();
                }
            }

            view.Scheduler!.ScheduleRepaint();
            return;
        }

        AllocationScope.Pause();
        try
        {
            RaisePainted(view);
            if (committed)
            {
                Capture?.Capture.NotifyDamaged(view.Output, new Box(0, 0, mode.Width, mode.Height));
            }
        }
        finally
        {
            AllocationScope.Resume();
        }

        if (committed)
        {
            view.FrameDoneAtFlip = true;
        }
        else
        {
            _scene.SendFrameDone((uint)Environment.TickCount);
        }

        if (ContinuousRepaint)
        {
            view.Scheduler!.ScheduleRepaint();
        }
    }

    private void SendFrameDoneAtFlip(OutputView view)
    {
        if (!view.FrameDoneAtFlip)
        {
            return;
        }

        view.FrameDoneAtFlip = false;
        _scene.SendFrameDone((uint)Environment.TickCount);
    }

    private void SecondaryRepaint(OutputView view, in OutputMode mode)
    {
        EnsureSwapchain(view, mode);
        if (view.Swapchain!.Acquire(out _) is not { } buffer)
        {
            return;
        }

        var replicating = view.ReplicaSource is not null;
        if (view.ReplicaSource is { } replicaSource)
        {
            if (!ReplicaBlit(replicaSource, buffer))
            {
                return;
            }
        }
        else
        {
            var rendered = _scene.Render(_renderer, buffer, new SceneRenderOptions
            {
                Background = Background,
                Projection = OutputProjection.For(view.Output),
                Luts = view.Scene,
                ColorDescription = view.Scene?.ColorDescription,
                OriginX = view.Box.X,
                OriginY = view.Box.Y,
            });
            if (!rendered)
            {
                return;
            }
        }

        _frameState.Clear();
        _frameState.SetBuffer(buffer);
        var committed = view.Output.Commit(_frameState);
        if (committed)
        {
            view.Scheduler!.NotifyCommitted();
            view.LastPresentedBuffer = buffer;
            view.Rendered++;
        }

        RaisePainted(view);
        Capture?.Capture.NotifyDamaged(view.Output, new Box(0, 0, mode.Width, mode.Height));
        if (replicating)
        {
            return;
        }

        if (committed)
        {
            view.FrameDoneAtFlip = true;
        }
        else
        {
            _scene.SendFrameDone((uint)Environment.TickCount);
        }
    }

    private bool ReplicaBlit(OutputView source, IBuffer target)
    {
        if ((source.LastPresentedBuffer ?? source.Scene?.LastTarget) is not { } sourceBuffer)
        {
            return false;
        }

        if (_renderer.ImportTexture(sourceBuffer) is not { } texture)
        {
            return false;
        }

        try
        {
            var scale = Math.Min(
                (double)target.Width / sourceBuffer.Width, (double)target.Height / sourceBuffer.Height);
            var width = (int)Math.Round(sourceBuffer.Width * scale);
            var height = (int)Math.Round(sourceBuffer.Height * scale);
            var pass = _renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddRect(new RenderColor(0f, 0f, 0f, 1f), new Box(0, 0, target.Width, target.Height));
            pass.AddTexture(texture, new TextureRenderOptions
            {
                DstBox = new Box((target.Width - width) / 2, (target.Height - height) / 2, width, height),
            });
            pass.Submit();
            return true;
        }
        finally
        {
            texture.Dispose();
        }
    }

    private Capabilities.ISurfaceHints? _foundHints;

    private Capabilities.ISurfaceHints? Hints() => SurfaceHints ?? (_foundHints ??= _host.Services?.Find<Capabilities.ISurfaceHints>());

    private void StampPacing(OutputView view)
    {
        var candidate = view.ScanoutCandidate;
        var tearing = AllowTearing && candidate is { IsDestroyed: false } && Hints()?.PrefersTearing(candidate) == true;
        if (tearing)
        {
            _frameState.SetTearing(true);
        }

        if (view.Scheduler is not { } scheduler)
        {
            return;
        }

        var adaptive = view.Output.AdaptiveSync;
        var arrival = VrrPacing switch
        {
            VrrPacingPolicy.Always => adaptive || tearing,
            VrrPacingPolicy.Fullscreen => tearing || (adaptive && (candidate is not null || view.Scene?.IsDirectScanout == true)),
            _ => false,
        };
        if (arrival != scheduler.ArrivalMode)
        {
            HostLog.Log.Debug($"{view.Output.Name}: arrival pacing {(arrival ? "on" : "off")} (adaptive sync {adaptive}, tearing {tearing})");
            scheduler.ArrivalMode = arrival;
        }
    }

    private void TrackRenderFence(OutputView view, long renderStart)
    {
        if (_renderer.FencePrecision == RenderFencePrecision.None)
        {
            return;
        }

        var fence = _renderer.ExportLastSubmissionFence();
        if (fence < 0)
        {
            return;
        }

        RenderFences.CloseFence(view.RenderFence);
        view.RenderFence = fence;
        view.RenderStartNanos = renderStart;
    }

    private static void ReportRenderTime(OutputView view)
    {
        if (view.RenderFence < 0)
        {
            return;
        }

        var signaled = RenderFences.SignalTimeNanos(view.RenderFence);
        if (signaled == 0 && !RenderFences.IsSignaled(view.RenderFence))
        {
            return;
        }

        if (signaled > view.RenderStartNanos)
        {
            view.Scheduler?.NotifyRenderTime(signaled - view.RenderStartNanos);
        }

        RenderFences.CloseFence(view.RenderFence);
        view.RenderFence = -1;
    }

    private void OracleRepaint(OutputView view)
    {
        if (!view.Output.Enabled)
        {
            return;
        }

        Frames?.BeginFrameAtNextRefresh(view.Output);
        if (!SyncViewGeometry(view, out var mode))
        {
            return;
        }

        _preparing = view;
        try
        {
            BeforeRepaint?.Invoke(view);
        }
        finally
        {
            _preparing = null;
        }

        EnsureSwapchain(view, mode);
        if (view.Swapchain!.Acquire(out _) is not { } target)
        {
            return;
        }

        _scene.Root.SetPosition(-view.Box.X, -view.Box.Y);
        var rendered = _scene.Render(_renderer, target, new SceneRenderOptions
        {
            Background = Background,
            Projection = OutputProjection.For(view.Output),
            Luts = view.Scene,
            ColorDescription = view.Scene?.ColorDescription,
        });
        _scene.Root.SetPosition(0, 0);
        if (!rendered)
        {
            return;
        }

        _frameState.Clear();
        StampFrame?.Invoke(view, _frameState);
        if (view.Output.Commit(_frameState.SetBuffer(target)))
        {
            view.Swapchain.Presented(target);
            view.LastPresentedBuffer = target;
            view.Rendered++;
        }

        RaisePainted(view);
        _scene.SendFrameDone((uint)Environment.TickCount);
    }
}
