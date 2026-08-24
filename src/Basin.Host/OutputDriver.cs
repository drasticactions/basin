using Basin.Backend.Drm;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Scene;
using Wayland.Server;

namespace Basin.Host;

public sealed class OutputDriver : IDisposable
{
    private readonly BasinHost _host;
    private readonly Scene.Scene _scene;
    private readonly OutputLayout _layout;
    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly List<OutputView> _views = [];
    private readonly List<IAllocator> _ownedAllocators = [];
    private readonly OutputState _frameState = new();

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
    }

    public SceneCapturePack? Capture { get; set; }

    public IFrameClock? Frames { get; set; }

    public RenderColor Background { get; set; } = new(0f, 0f, 0f, 1f);

    public int Requested { get; set; } = 1;

    public double[] Scales { get; set; } = [];

    public OutputMode HeadlessMode { get; set; } = new(1280, 720, 60_000);

    public Func<int, string> NestedName { get; set; } = index => $"basin-{index + 1}";

    public bool ContinuousRepaint { get; set; }

    public bool LastOnly { get; set; }

    public Action<IReadOnlyList<OutputView>>? Arrange { get; set; }

    public IReadOnlyList<OutputView> Views => _views;

    public long PrimaryRendered => _views.Count > 0 ? _views[0].Rendered : 0;

    public event Action<OutputView>? Added;

    public event Action<OutputView>? Removed;

    public event Action? Emptied;

    public event Action<OutputView>? BeforeRepaint;

    public event Action<OutputView>? Painted;

    public event Action<OutputView>? ModeChanged;

    public event Action? LayoutChanged;

    public event Action<OutputView, ScanoutChoice>? ScanoutChanged;

    public event Action<OutputView>? HostScaleFollowed;

    public event Action<OutputView, double>? ScaleRefused;

    public event Action<DrmOutput>? ModesetRefused;

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

    public OutputView AddView(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (LastOnly && _views.Count >= 1)
        {
            Disable(_views[^1]);
        }

        var view = new OutputView(output, new OutputGlobal(_host.Display, output));
        var index = _views.Count;
        _views.Add(view);

        var scale = Scales.Length == 0 ? 1 : Scales[Math.Min(index, Scales.Length - 1)];
        if (scale != output.Scale)
        {
            using var state = new OutputState();
            if (!output.Commit(state.SetScale(scale)))
            {
                ScaleRefused?.Invoke(view, scale);
            }
        }

        view.Scale = output.Scale;
        _layout.Add(output, 0, 0);
        Relayout();
        ChooseScanout(view);
        WireView(view);
        if (Scales.Length == 0 && output is WaylandOutput hosted)
        {
            hosted.HostScaleChanged += () => FollowHostScale(view, hosted);
        }

        Added?.Invoke(view);
        return view;
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
        Teardown(view);

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
        foreach (var view in _views)
        {
            Teardown(view);
        }

        _views.Clear();
        foreach (var allocator in _ownedAllocators)
        {
            allocator.Dispose();
        }

        _ownedAllocators.Clear();
        _frameState.Dispose();
    }

    public static bool EnableWithMode(DrmOutput card)
    {
        ArgumentNullException.ThrowIfNull(card);

        using var state = new OutputState();
        state.SetEnabled(true).SetMode(card.PreferredMode);
        if (card.TestCommit(state))
        {
            return card.Commit(state);
        }

        foreach (var mode in card.Modes)
        {
            if (mode == card.PreferredMode)
            {
                continue;
            }

            state.Clear();
            state.SetEnabled(true).SetMode(mode);
            if (card.TestCommit(state))
            {
                return card.Commit(state);
            }
        }

        return false;
    }

    private void OnNewDrmOutput(DrmOutput card)
    {
        if (!EnableWithMode(card))
        {
            ModesetRefused?.Invoke(card);
            return;
        }

        AddView(card);
    }

    private void Teardown(OutputView view)
    {
        view.Scheduler?.Dispose();
        view.Scene?.Dispose();
        view.Swapchain?.Dispose();
        view.Global.Dispose();
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
        var scheduler = new OutputScheduler(_host.Loop, view.Output);
        view.Scheduler = scheduler;
        var sceneOutput = new SceneOutput(_scene, view.Output);
        view.Scene = sceneOutput;
        scheduler.Repaint += () => Repaint(view);
        sceneOutput.DamagePending += scheduler.ScheduleRepaint;
        view.Output.Committed += _ => scheduler.ScheduleRepaint();
        Capture?.DmabufCapture.Track(view.Output, sceneOutput);
        if (view.Output is DrmOutput presenting)
        {
            presenting.PresentedOnScreen += (timeNs, _, _) => scheduler.NotifyPresented((long)timeNs);
        }

        if (view.Output is WaylandOutput nested)
        {
            nested.CloseRequested += () => RemoveView(view);
        }

        scheduler.ScheduleRepaint();
    }

    private void Repaint(OutputView view)
    {
        if (!view.Output.Enabled)
        {
            return;
        }

        Frames?.BeginFrame(view.Output, view.Scheduler?.PredictedVblankNanos ?? 0);
        var mode = view.Output.CurrentMode;
        if (mode.Width <= 0 || mode.Height <= 0)
        {
            return;
        }

        if (mode.Width != view.Width || mode.Height != view.Height || view.Scale != view.Output.Scale)
        {
            (view.Width, view.Height) = (mode.Width, mode.Height);
            view.Scale = view.Output.Scale;
            Relayout();
            ModeChanged?.Invoke(view);
        }

        BeforeRepaint?.Invoke(view);

        if (view.Swapchain is null)
        {
            view.Swapchain = new Swapchain(
                view.Allocator!, mode.Width, mode.Height, DrmFormat.Xrgb8888, view.SwapModifiers);
        }
        else if (mode.Width != view.Swapchain.Width || mode.Height != view.Swapchain.Height)
        {
            view.Swapchain.Resize(mode.Width, mode.Height);
        }

        if (ContinuousRepaint)
        {
            view.Scene!.Ring.AddWhole();
        }

        view.Scene!.Position = new Point(view.Box.X, view.Box.Y);

        _frameState.Clear();
        var committed = view.Scene.Commit(
            _renderer, view.Swapchain, _frameState, new SceneCommitOptions
            {
                Background = Background,
                TargetPresentNanos = Math.Max(view.Scheduler!.PredictedVblankNanos, MonotonicClock.Nanos),
            });
        if (committed)
        {
            view.Scheduler!.NotifyCommitted();
            view.LastPresentedBuffer = _frameState.Buffer;
            view.Rendered++;
        }
        else if (view.Scene.NeedsRepaint)
        {
            if (view.Rendered == 0 && _host.Drm is { } drm && view.Allocator is not DumbAllocator)
            {
                ScanoutChanged?.Invoke(view, ScanoutChoice.RefusedByPlane);
                view.Swapchain.Dispose();
                view.Swapchain = null;
                var dumb = new DumbAllocator(drm);
                _ownedAllocators.Add(dumb);
                view.Allocator = dumb;
                view.SwapModifiers = [DrmFormatSet.ModifierLinear];
            }

            view.Scheduler!.ScheduleRepaint();
            return;
        }

        Painted?.Invoke(view);
        if (committed)
        {
            Capture?.Capture.NotifyDamaged(view.Output, new Box(0, 0, mode.Width, mode.Height));
        }

        _scene.SendFrameDone((uint)Environment.TickCount);
        if (ContinuousRepaint)
        {
            view.Scheduler!.ScheduleRepaint();
        }
    }
}
