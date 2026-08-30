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
    private readonly Dictionary<OutputView, Action<SceneNode?, Box>> _secondaryDamage = [];
    private bool? _hasLidSwitch;

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

    public bool AllowDirectScanout { get; set; } = true;

    public bool AllowPlaneOffload { get; set; } = true;

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

    public bool DebugDamageTint { get; set; }

    public Action<IReadOnlyList<OutputView>>? Arrange { get; set; }

    public IReadOnlyList<OutputView> Views => _views;

    public long PrimaryRendered => _views.Count > 0 ? _views[0].Rendered : 0;

    public event Action<OutputView>? Added;

    public event Action<OutputView>? Removed;

    public event Action? Emptied;

    public event Action<OutputView>? BeforeRepaint;

    public event Action<OutputView>? Painted;

    public event Action<OutputView>? ModeChanged;

    public event Action<OutputView, OutputTransform>? TransformChanged;

    public event Action<OutputView, OutputState>? StampFrame;

    public event Action<DrmOutput, OutputState>? StampModeset;

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

    public OutputView AddView(IOutput output) => AddView(output, null, secondary: false);

    public OutputView AddView(IOutput output, IAllocator? allocator, bool secondary = false)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (LastOnly && _views.Count >= 1)
        {
            Disable(_views[^1]);
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

    public void SetAspectRatio(OutputView view, double aspectRatio)
    {
        ArgumentNullException.ThrowIfNull(view);

        using var state = new OutputState();
        if (!view.Output.Commit(state.SetAspectRatio(aspectRatio)))
        {
            return;
        }

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

    public static bool EnableWithMode(DrmOutput card) => EnableWithMode(card, null);

    public bool EnableWithStamp(DrmOutput card) => EnableWithMode(card, StampModeset);

    private static bool EnableWithMode(DrmOutput card, Action<DrmOutput, OutputState>? stamp)
    {
        ArgumentNullException.ThrowIfNull(card);

        using var state = new OutputState();
        state.SetEnabled(true).SetMode(card.PreferredMode);
        stamp?.Invoke(card, state);
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
        if (!EnableWithMode(card, StampModeset))
        {
            ModesetRefused?.Invoke(card);
            return;
        }

        AddView(card);
    }

    private void Teardown(OutputView view)
    {
        if (_secondaryDamage.Remove(view, out var damage))
        {
            _scene.Damaged -= damage;
        }

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
        }

        if (FullRepaint)
        {
            view.Output.Frame += () => OracleRepaint(view);
            return;
        }

        var scheduler = new OutputScheduler(_host.Loop, view.Output);
        view.Scheduler = scheduler;
        scheduler.Repaint += () => Repaint(view);
        view.Output.Committed += _ => scheduler.ScheduleRepaint();
        if (view.Output is IPresentingOutput presenting)
        {
            presenting.PresentedOnScreen += (timeNs, _, _) => scheduler.NotifyPresented((long)timeNs);
        }

        if (view.IsSecondary)
        {
            void OnDamage(SceneNode? _, Box box)
            {
                var watched = view.ReplicaSource is { } replicaSource ? replicaSource.Box : view.Box;
                if (!watched.IsEmpty && box.X < watched.X + watched.Width && box.X + box.Width > watched.X &&
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

        var sceneOutput = new SceneOutput(_scene, view.Output);
        view.Scene = sceneOutput;
        sceneOutput.DamagePending += scheduler.ScheduleRepaint;
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

        return true;
    }

    private void EnsureSwapchain(OutputView view, in OutputMode mode)
    {
        if (view.Swapchain is null)
        {
            view.Swapchain = new Swapchain(
                view.Allocator!, mode.Width, mode.Height, DrmFormat.Xrgb8888, view.SwapModifiers);
        }
        else if (mode.Width != view.Swapchain.Width || mode.Height != view.Swapchain.Height)
        {
            view.Swapchain.Resize(mode.Width, mode.Height);
        }
    }

    private void Repaint(OutputView view)
    {
        if (!view.Output.Enabled)
        {
            return;
        }

        Frames?.BeginFrame(view.Output, view.Scheduler?.PredictedVblankNanos ?? 0);
        if (!SyncViewGeometry(view, out var mode))
        {
            return;
        }

        if (view.IsSecondary)
        {
            SecondaryRepaint(view, mode);
            return;
        }

        BeforeRepaint?.Invoke(view);

        EnsureSwapchain(view, mode);

        if (ContinuousRepaint)
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
        StampFrame?.Invoke(view, _frameState);
        var committed = view.Scene.Commit(
            _renderer, view.Swapchain!, _frameState, new SceneCommitOptions
            {
                Background = Background,
                DebugDamageTint = DebugDamageTint,
                AllowDirectScanout = AllowDirectScanout,
                AllowPlaneOffload = AllowPlaneOffload,
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
                view.Swapchain!.Dispose();
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
            _scene.Root.SetPosition(-view.Box.X, -view.Box.Y);
            var rendered = _scene.Render(_renderer, buffer, new SceneRenderOptions
            {
                Background = Background,
                Projection = OutputProjection.For(view.Output),
            });
            _scene.Root.SetPosition(0, 0);
            if (!rendered)
            {
                return;
            }
        }

        _frameState.Clear();
        _frameState.SetBuffer(buffer);
        if (view.Output.Commit(_frameState))
        {
            view.Scheduler!.NotifyCommitted();
            view.LastPresentedBuffer = buffer;
            view.Rendered++;
        }

        Painted?.Invoke(view);
        Capture?.Capture.NotifyDamaged(view.Output, new Box(0, 0, mode.Width, mode.Height));
        if (!replicating)
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

        BeforeRepaint?.Invoke(view);
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

        Painted?.Invoke(view);
        _scene.SendFrameDone((uint)Environment.TickCount);
    }
}
