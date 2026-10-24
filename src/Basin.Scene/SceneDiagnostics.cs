using System.Globalization;
using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.Scene;

/// <summary>
/// Implements <see cref="ISceneDiagnostics"/> over a <see cref="Scene"/> and, when given, the outputs that a driver presents.
/// </summary>
public sealed class SceneDiagnostics : ISceneDiagnostics
{
    private readonly Scene _scene;
    private readonly IRenderer _renderer;
    private readonly ICompositorEventLoop _loop;
    private readonly IPresentedOutputs? _outputs;
    private IFrameClock? _clock;
    private IToplevelModel? _toplevels;
    private readonly List<DamageWatcher> _damageWatchers = [];
    private readonly List<FrameWatcher> _frameWatchers = [];
    private readonly List<PlaneWatcher> _planeWatchers = [];
    private readonly Dictionary<string, PlaneReport> _lastPlanes = new(StringComparer.Ordinal);
    private Recording? _recording;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneDiagnostics"/> class.
    /// </summary>
    /// <param name="scene">The scene to read.</param>
    /// <param name="renderer">The renderer that reads presented buffers back.</param>
    /// <param name="loop">The loop that times recordings.</param>
    /// <param name="outputs">The presented outputs, or <see langword="null"/>. Without them, plane reports are empty and recordings cannot start.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/>, <paramref name="renderer"/> or <paramref name="loop"/> is <see langword="null"/>.</exception>
    public SceneDiagnostics(Scene scene, IRenderer renderer, ICompositorEventLoop loop, IPresentedOutputs? outputs)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(loop);
        _scene = scene;
        _renderer = renderer;
        _loop = loop;
        _outputs = outputs;
    }

    /// <summary>
    /// Gets or sets the services to look up <see cref="Clock"/> and <see cref="Toplevels"/> in when they are not set directly.
    /// </summary>
    public BasinServices? Services { get; set; }

    /// <summary>
    /// Gets or sets the frame clock. When it is not set, the getter finds one in <see cref="Services"/>.
    /// </summary>
    public IFrameClock? Clock
    {
        get => _clock ?? Services?.Find<IFrameClock>();
        set => _clock = value;
    }

    /// <inheritdoc/>
    public int ClockSinks => Clock is Capabilities.Defaults.FrameClock clock ? clock.SinkCount : -1;

    /// <summary>
    /// Gets or sets the toplevel model that names frames and follows windows. When it is not set, the getter finds one in <see cref="Services"/>.
    /// </summary>
    public IToplevelModel? Toplevels
    {
        get => _toplevels ?? Services?.Find<IToplevelModel>();
        set => _toplevels = value;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SceneNodeInfo> Tree() => SceneDump.Nodes(_scene.Root);

    /// <inheritdoc/>
    public IReadOnlyList<FrameInfo> Frames()
    {
        var registered = _scene.Frames;
        var frames = new FrameInfo[registered.Count];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = Info(registered[i]);
        }

        return frames;
    }

    /// <summary>
    /// Formats a frame snapshot as one line of key=value fields, with invariant culture.
    /// </summary>
    /// <param name="prefix">The text before the frame name.</param>
    /// <param name="frame">The frame snapshot.</param>
    /// <returns>The formatted line.</returns>
    public static string Describe(string prefix, in FrameInfo frame)
    {
        var outer = frame.Outer;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix} {frame.Name} draws={frame.Draws} animating={frame.Animating} visible={frame.Visible}"
            + $" outer={outer.X},{outer.Y},{outer.Width}x{outer.Height} active={frame.Active} maximized={frame.Maximized}"
            + $" resizing={frame.Resizing} hot={frame.Hot} previous-hot={frame.PreviousHot} pressed={frame.Pressed}"
            + $" inside={frame.Inside} now-ms={frame.NowMs} hover-ms={frame.HoverMs} active-ms={frame.ActiveMs}");
    }

    /// <inheritdoc/>
    public IDisposable WatchDamage(bool stacks, Action<DamageInfo> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var watcher = new DamageWatcher(this, stacks ? [] : null, sink);
        _damageWatchers.Add(watcher);
        if (_damageWatchers.Count == 1)
        {
            _scene.Damaged += OnDamaged;
        }

        return watcher;
    }

    /// <inheritdoc/>
    public IDisposable WatchFrames(Action<FrameInfo> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var watcher = new FrameWatcher(this, sink);
        _frameWatchers.Add(watcher);
        if (_frameWatchers.Count == 1)
        {
            _scene.FrameDrawn += OnFrameDrawn;
        }

        return watcher;
    }

    /// <inheritdoc/>
    public Func<Box?>? LocateWindow(Surface surface, int margin)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (Find(surface) is not { } frame)
        {
            return LocateSurface(surface, margin);
        }

        var margined = Math.Max(0, margin);
        return () => frame.Tree.IsDestroyed ? null : Margined(SceneOuter(frame), margined);
    }

    /// <inheritdoc/>
    public Func<Box?>? LocateSurface(Surface surface, int margin)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (FindBuffer(_scene.Root, surface) is null)
        {
            return null;
        }

        var margined = Math.Max(0, margin);
        return () => FindBuffer(_scene.Root, surface) is { } buffer && Bounds(buffer.Parent ?? (SceneNode)buffer) is { IsEmpty: false } box
            ? Margined(box, margined)
            : null;
    }

    /// <inheritdoc/>
    public Func<Box?>? LocateNode(string path, int margin, out string? error)
    {
        ArgumentNullException.ThrowIfNull(path);
        var found = new List<SceneNode>();
        Collect(_scene.Root, path, found);
        if (found.Count != 1)
        {
            error = found.Count == 0 ? $"No node is at '{path}'." : $"'{path}' names {found.Count} nodes. Give the node you want a unique DebugName.";
            return null;
        }

        error = null;
        var node = found[0];
        var margined = Math.Max(0, margin);
        return () => node.IsDestroyed || Bounds(node) is not { IsEmpty: false } box ? null : Margined(box, margined);
    }

    private static void Collect(SceneNode node, string path, List<SceneNode> found)
    {
        if (SceneDump.PathOf(node) == path)
        {
            found.Add(node);
        }

        if (node is SceneTree tree)
        {
            foreach (var child in tree.Children)
            {
                Collect(child, path, found);
            }
        }
    }

    private static SceneBuffer? FindBuffer(SceneNode node, Surface surface)
    {
        if (node is SceneBuffer buffer && buffer.InputSurface == surface)
        {
            return buffer;
        }

        if (node is SceneTree tree)
        {
            foreach (var child in tree.Children)
            {
                if (FindBuffer(child, surface) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static Box? Bounds(SceneNode node)
    {
        if (!node.Enabled)
        {
            return null;
        }

        Box? bounds = node switch
        {
            SceneBuffer buffer when buffer.Size is ({ } width, { } height) && width > 0 && height > 0 && !buffer.BackdropOnly =>
                node.MapToScene(new Box(0, 0, width, height)),
            SceneRect rect when rect.Width > 0 && rect.Height > 0 => node.MapToScene(new Box(0, 0, rect.Width, rect.Height)),
            _ => null,
        };
        if (node is SceneTree tree)
        {
            foreach (var child in tree.Children)
            {
                if (Bounds(child) is { } inner)
                {
                    bounds = bounds is { } outer ? Union(outer, inner) : inner;
                }
            }
        }

        return bounds;
    }

    private static Box Union(in Box a, in Box b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        return new Box(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    /// <inheritdoc/>
    public string? Record(RecordRequest request, Action<RecordingResult> done)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(done);
        if (_outputs is null)
        {
            return "No output has presented a frame yet. Record after the first frame.";
        }

        if (request.Frames <= 0)
        {
            return "The frame count must be 1 or more.";
        }

        SceneOutput? view = null;
        Box region;
        if (request.Follow is { } follow)
        {
            if (follow() is not { } scene || scene.IsEmpty)
            {
                return "The target has no size, so there is nothing to record.";
            }

            var centerX = scene.X + (scene.Width / 2);
            var centerY = scene.Y + (scene.Height / 2);
            foreach (var candidate in _outputs.Outputs)
            {
                var area = SceneArea(candidate);
                if ((request.Output is null || candidate.Output.Name == request.Output) &&
                    centerX >= area.X && centerY >= area.Y && centerX < area.Right && centerY < area.Bottom)
                {
                    view = candidate;
                    break;
                }
            }

            if (view is null)
            {
                return "The target is not on an output. Show it on an output, then record it.";
            }

            region = Physical(view, scene);
        }
        else
        {
            foreach (var candidate in _outputs.Outputs)
            {
                if (request.Output is null || candidate.Output.Name == request.Output)
                {
                    view = candidate;
                    break;
                }
            }

            if (view is null)
            {
                return request.Output is null ? "No output is available to record." : $"No output is named '{request.Output}'. Call outputs/list for the names.";
            }

            region = request.Region;
        }

        if (region.IsEmpty)
        {
            return "The region is empty. Give a region with a width and a height.";
        }

        _recording?.Finish(timedOut: true);
        _recording = new Recording(this, view, region, request, done);
        _recording.Start();
        return null;
    }

    /// <inheritdoc/>
    public bool WriteParts(Surface surface, Action<FramePart, Box> part)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(part);
        if (Find(surface) is not { } frame)
        {
            return false;
        }

        var outer = frame.OuterBounds;
        var extents = new Dictionary<FramePart, (int Left, int Top, int Right, int Bottom)>();
        for (var y = outer.Y; y < outer.Bottom; y++)
        {
            for (var x = outer.X; x < outer.Right; x++)
            {
                var found = frame.PartAt(x + 0.5, y + 0.5);
                if (found == FramePart.None)
                {
                    continue;
                }

                extents[found] = extents.TryGetValue(found, out var e)
                    ? (Math.Min(e.Left, x), Math.Min(e.Top, y), Math.Max(e.Right, x + 1), Math.Max(e.Bottom, y + 1))
                    : (x, y, x + 1, y + 1);
            }
        }

        foreach (var (found, e) in extents.OrderBy(static pair => pair.Key))
        {
            part(found, ToScene(frame, new Box(e.Left, e.Top, e.Right - e.Left, e.Bottom - e.Top)));
        }

        return true;
    }

    /// <inheritdoc/>
    public void AllowPlanes(bool allowed) => _outputs?.AllowPlanes(allowed);

    /// <inheritdoc/>
    public IReadOnlyList<PlaneReport> Planes()
    {
        if (_outputs is null)
        {
            return [];
        }

        var reports = new List<PlaneReport>();
        foreach (var output in _outputs.Outputs)
        {
            reports.Add(Report(output));
        }

        return reports;
    }

    /// <inheritdoc/>
    public IDisposable WatchPlanes(Action<PlaneReport> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var watcher = new PlaneWatcher(this, sink);
        _planeWatchers.Add(watcher);
        if (_planeWatchers.Count == 1 && _outputs is { } outputs)
        {
            outputs.Presented += OnPlanesPresented;
        }

        return watcher;
    }

    /// <summary>
    /// Builds a plane report from the latest commit of an output: the nodes on planes, then the declined candidates with their reasons.
    /// </summary>
    /// <param name="output">The output to report on.</param>
    /// <returns>The report. Each entry's box is in scene coordinates, and an entry on a plane also has its plane box in output physical pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static PlaneReport Report(SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var entries = new List<PlaneDecision>();
        var nodes = output.OffloadedNodes;
        var boxes = output.OffloadedBoxes;
        for (var i = 0; i < nodes.Count; i++)
        {
            entries.Add(new PlaneDecision(SceneDump.PathOf(nodes[i]), SceneBox(nodes[i]), OnPlane: true, Reason: null, PlaneBox: boxes[i]));
        }

        var declined = output.DeclinedCandidates;
        var reasons = output.DeclineReasons;
        for (var i = 0; i < declined.Count; i++)
        {
            var node = declined[i];
            entries.Add(new PlaneDecision(SceneDump.PathOf(node), SceneBox(node), OnPlane: false, Reason: Reason(reasons[i])));
        }

        return new PlaneReport(output.Output.Name, output.OffloadSkipped, entries);
    }

    private static Box SceneBox(SceneNode node)
    {
        var (width, height) = node is SceneBuffer buffer ? buffer.DisplaySize : (0, 0);
        return node.MapToScene(new Box(0, 0, width, height));
    }

    private static string Reason(PlaneDeclineReason reason) => reason switch
    {
        PlaneDeclineReason.BlendSpace => "blend-space",
        PlaneDeclineReason.BackendRefused => "backend-refused",
        PlaneDeclineReason.YuvEncoding => "yuv-encoding",
        _ => reason.ToString().ToLowerInvariant(),
    };

    private void OnPlanesPresented(SceneOutput output)
    {
        AllocationScope.Pause();
        try
        {
            var report = Report(output);
            if (report.SameAs(_lastPlanes.GetValueOrDefault(output.Output.Name)))
            {
                return;
            }

            _lastPlanes[output.Output.Name] = report;
            foreach (var watcher in _planeWatchers.ToArray())
            {
                watcher.Sink(report);
            }
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private sealed class PlaneWatcher(SceneDiagnostics owner, Action<PlaneReport> sink) : IDisposable
    {
        public Action<PlaneReport> Sink { get; } = sink;

        public void Dispose()
        {
            if (owner._planeWatchers.Remove(this) && owner._planeWatchers.Count == 0)
            {
                if (owner._outputs is { } outputs)
                {
                    outputs.Presented -= owner.OnPlanesPresented;
                }

                owner._lastPlanes.Clear();
            }
        }
    }

    /// <summary>
    /// Maps a box in scene coordinates to the physical pixels of an output, rounded outward. A replicated output uses the area of its source.
    /// </summary>
    /// <param name="output">The output to map to.</param>
    /// <param name="scene">The box in scene logical pixels.</param>
    /// <returns>The box in the output's physical pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static Box Physical(SceneOutput output, in Box scene)
    {
        ArgumentNullException.ThrowIfNull(output);
        var area = SceneArea(output);
        return output.Projection.ProjectExpanded(scene.Translated(-area.X, -area.Y));
    }

    private FrameInfo Info(Frame frame)
    {
        var state = frame.DrawnState;
        var interaction = frame.DrawnInteraction;
        return new FrameInfo(
            NameOf(frame),
            frame.Window,
            frame.DrawCount,
            frame.Animating,
            frame.Visible,
            frame.OuterBounds,
            state.Active,
            state.Maximized,
            state.Resizing,
            interaction.Hot,
            interaction.PreviousHot,
            interaction.Pressed,
            interaction.PointerInside,
            interaction.Now / 1_000_000,
            interaction.HoverChangedAt / 1_000_000,
            interaction.ActiveChangedAt / 1_000_000);
    }

    private static Box SceneOuter(Frame frame) => ToScene(frame, frame.OuterBounds);

    private static Box ToScene(Frame frame, in Box local) => frame.Tree.Parent?.MapToScene(local) ?? local;

    private static Box Margined(in Box box, int margin) =>
        new(box.X - margin, box.Y - margin, box.Width + (2 * margin), box.Height + (2 * margin));

    private static Box SceneArea(SceneOutput output)
    {
        if (output.ReplicationSource is { } source)
        {
            return source;
        }

        var projection = output.Projection;
        return new Box(
            output.Position.X,
            output.Position.Y,
            (int)Math.Round(projection.Width / projection.Scale),
            (int)Math.Round(projection.Height / projection.Scale));
    }

    private string NameOf(Frame frame)
    {
        if (frame.Window is { } surface && Toplevels is { } model && model.TryFind(surface, out var info))
        {
            return string.IsNullOrEmpty(info.AppId) ? info.Title : info.AppId;
        }

        return frame.Tree.Parent?.DebugName ?? "frame";
    }

    private Frame? Find(Surface surface)
    {
        foreach (var frame in _scene.Frames)
        {
            if (frame.Window == surface && !frame.Tree.IsDestroyed)
            {
                return frame;
            }
        }

        return null;
    }

    private void OnFrameDrawn(Frame frame)
    {
        AllocationScope.Pause();
        try
        {
            var info = Info(frame);
            foreach (var watcher in _frameWatchers.ToArray())
            {
                watcher.Sink(info);
            }
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private void OnDamaged(SceneNode? source, Box box)
    {
        AllocationScope.Pause();
        try
        {
            var path = SceneDump.PathOf(source);
            string? stack = null;
            foreach (var watcher in _damageWatchers.ToArray())
            {
                string? sent = null;
                if (watcher.Seen is { } seen && seen.Add(path))
                {
                    sent = stack ??= new System.Diagnostics.StackTrace(1, false).ToString();
                }

                watcher.Sink(new DamageInfo(path, box, sent));
            }
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private sealed class DamageWatcher(SceneDiagnostics owner, HashSet<string>? seen, Action<DamageInfo> sink) : IDisposable
    {
        public HashSet<string>? Seen { get; } = seen;

        public Action<DamageInfo> Sink { get; } = sink;

        public void Dispose()
        {
            if (owner._damageWatchers.Remove(this) && owner._damageWatchers.Count == 0)
            {
                owner._scene.Damaged -= owner.OnDamaged;
            }
        }
    }

    private sealed class FrameWatcher(SceneDiagnostics owner, Action<FrameInfo> sink) : IDisposable
    {
        public Action<FrameInfo> Sink { get; } = sink;

        public void Dispose()
        {
            if (owner._frameWatchers.Remove(this) && owner._frameWatchers.Count == 0)
            {
                owner._scene.FrameDrawn -= owner.OnFrameDrawn;
            }
        }
    }

    private sealed class Recording(SceneDiagnostics owner, SceneOutput view, Box region, RecordRequest request, Action<RecordingResult> done)
    {
        private readonly int _capacity = Math.Min(request.Frames, 600);
        private readonly List<RecordFrameStats> _stats = [];
        private readonly byte[] _strip = new byte[region.Width * region.Height * Math.Min(request.Frames, 600) * 4];
        private Box _box = region;
        private byte[]? _previous;
        private const int NudgeMs = 100;

        private IEventSource? _timeout;
        private IEventSource? _nudge;
        private bool _finished;

        public void Start()
        {
            var outputs = owner._outputs!;
            outputs.Presented += OnPresented;
            outputs.CapturePresented = true;
            _timeout = owner._loop.AddTimer(() => Finish(timedOut: true));
            _timeout.UpdateTimer(Math.Max(1, request.TimeoutMs));
            _nudge = owner._loop.AddTimer(Nudge);
            _nudge.UpdateTimer(NudgeMs);
            if (request.Immediate && outputs.TryReadPresented(view, owner._renderer, out _, out _, out _))
            {
                OnPresented(view);
            }
        }

        public void Finish(bool timedOut)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            owner._outputs!.Presented -= OnPresented;
            owner._outputs.CapturePresented = false;
            _timeout?.Remove();
            _timeout = null;
            _nudge?.Remove();
            _nudge = null;
            if (ReferenceEquals(owner._recording, this))
            {
                owner._recording = null;
            }

            var frames = _stats.Count;
            var (imageWidth, imageHeight) = (_box.Width, _box.Height * frames);
            byte[]? kept = null;
            if (frames > 0 && request.KeepPixels)
            {
                kept = _strip.AsSpan(0, _box.Width * _box.Height * frames * 4).ToArray();
            }

            byte[]? encoded = null;
            if (frames > 0 && (!string.IsNullOrEmpty(request.Path) || request.InlinePng))
            {
                var rgba = _strip.AsSpan(0, _box.Width * _box.Height * frames * 4);
                byte[] png;
                if (request.MaxDimension > 0 && Math.Max(_box.Width, _box.Height) > request.MaxDimension)
                {
                    var factor = (double)request.MaxDimension / Math.Max(_box.Width, _box.Height);
                    var bandWidth = Math.Max(1, (int)Math.Round(_box.Width * factor));
                    var bandHeight = Math.Max(1, (int)Math.Round(_box.Height * factor));
                    var scaled = Downscale(rgba, _box.Width, _box.Height, frames, bandWidth, bandHeight);
                    (imageWidth, imageHeight) = (bandWidth, bandHeight * frames);
                    png = PngCodec.Encode(scaled, imageWidth, imageHeight);
                }
                else
                {
                    png = PngCodec.Encode(rgba, imageWidth, imageHeight);
                }

                if (!string.IsNullOrEmpty(request.Path))
                {
                    File.WriteAllBytes(request.Path, png);
                }

                encoded = request.InlinePng ? png : null;
            }
            else if (frames == 0)
            {
                (imageWidth, imageHeight) = (0, 0);
            }

            var lumaLow = frames == 0 ? 0 : _stats.Min(static s => s.Luma);
            var lumaHigh = frames == 0 ? 0 : _stats.Max(static s => s.Luma);
            var peakLow = frames == 0 ? 0 : _stats.Min(static s => s.Peak);
            var peakHigh = frames == 0 ? 0 : _stats.Max(static s => s.Peak);
            var deltaMax = frames == 0 ? 0 : _stats.Max(static s => s.Delta);
            done(new RecordingResult(
                request.Path, frames, _box.Width, _box.Height, imageWidth, imageHeight,
                lumaLow, lumaHigh, peakLow, peakHigh, deltaMax, timedOut && frames < _capacity, _stats.ToArray())
            {
                Pixels = kept,
                Png = encoded,
            });
        }

        private void OnPresented(SceneOutput painted)
        {
            if (_finished || painted != view)
            {
                return;
            }

            var unmapped = false;
            if (request.Follow?.Invoke() is { } scene)
            {
                var physical = Physical(view, scene);
                var mode = view.Output.CurrentMode;
                var context = new PostContext(mode.Width, mode.Height, default);
                foreach (var stage in view.PostStages)
                {
                    if (stage.TryMapBox(physical, context, out var mapped))
                    {
                        physical = mapped;
                    }
                    else
                    {
                        unmapped = true;
                    }
                }

                _box = Centered(physical, _box.Width, _box.Height);
            }

            _nudge?.UpdateTimer(NudgeMs);
            var index = _stats.Count;
            var box = _box;
            var planes = new List<Box>();
            if (owner._outputs!.TryReadPresented(view, owner._renderer, out var rgba, out var bufferWidth, out var bufferHeight))
            {
                var left = Math.Max(0, -box.X);
                var columns = Math.Min(box.Width, bufferWidth - box.X) - left;
                for (var y = Math.Max(0, -box.Y); y < box.Height && box.Y + y < bufferHeight && columns > 0; y++)
                {
                    rgba.AsSpan((((box.Y + y) * bufferWidth) + box.X + left) * 4, columns * 4)
                        .CopyTo(_strip.AsSpan(((((index * box.Height) + y) * box.Width) + left) * 4));
                }
            }

            if (view.PresentedLayers is { } layers)
            {
                foreach (var layer in layers)
                {
                    if (!layer.Accepted || layer.Buffer is not { } layerBuffer)
                    {
                        continue;
                    }

                    planes.Add(layer.DstBox);
                    if (BufferCapture.TryReadRgba(layerBuffer, owner._renderer, out var plane))
                    {
                        Blend(_strip, box, index, plane, layerBuffer.Width, layerBuffer.Height, layer);
                    }
                }
            }

            var slice = _strip.AsSpan(index * box.Width * box.Height * 4, box.Width * box.Height * 4);
            var (mean, peak) = Luma(slice);
            var delta = _previous is { } previous ? Changed(previous, slice) : 0;
            _previous = slice.ToArray();
            _stats.Add(new RecordFrameStats(index, box, mean, peak, delta, planes, unmapped));
            if (_stats.Count >= _capacity)
            {
                Finish(timedOut: false);
            }
        }

        private void Nudge()
        {
            if (_finished)
            {
                return;
            }

            owner._outputs!.RequestRepaint(view);
            _nudge?.UpdateTimer(NudgeMs);
        }

        private static Box Centered(in Box around, int width, int height) =>
            new(around.X + ((around.Width - width) / 2), around.Y + ((around.Height - height) / 2), width, height);

        private static byte[] Downscale(ReadOnlySpan<byte> rgba, int width, int height, int bands, int toWidth, int toHeight)
        {
            var scaled = new byte[toWidth * toHeight * bands * 4];
            for (var band = 0; band < bands; band++)
            {
                for (var y = 0; y < toHeight; y++)
                {
                    var y0 = y * height / toHeight;
                    var y1 = Math.Max(y0 + 1, (y + 1) * height / toHeight);
                    for (var x = 0; x < toWidth; x++)
                    {
                        var x0 = x * width / toWidth;
                        var x1 = Math.Max(x0 + 1, (x + 1) * width / toWidth);
                        int r = 0, g = 0, b = 0, a = 0, n = 0;
                        for (var sy = y0; sy < y1; sy++)
                        {
                            for (var sx = x0; sx < x1; sx++)
                            {
                                var i = ((((band * height) + sy) * width) + sx) * 4;
                                r += rgba[i];
                                g += rgba[i + 1];
                                b += rgba[i + 2];
                                a += rgba[i + 3];
                                n++;
                            }
                        }

                        var o = ((((band * toHeight) + y) * toWidth) + x) * 4;
                        scaled[o] = (byte)(r / n);
                        scaled[o + 1] = (byte)(g / n);
                        scaled[o + 2] = (byte)(b / n);
                        scaled[o + 3] = (byte)(a / n);
                    }
                }
            }

            return scaled;
        }

        private static void Blend(byte[] strip, Box box, int frame, byte[] plane, int planeWidth, int planeHeight, OutputLayer layer)
        {
            var dst = layer.DstBox;
            var src = layer.SrcBox.IsEmpty ? new FBox(0, 0, planeWidth, planeHeight) : layer.SrcBox;
            for (var y = 0; y < box.Height; y++)
            {
                var screenY = box.Y + y;
                if (screenY < dst.Y || screenY >= dst.Bottom)
                {
                    continue;
                }

                var sy = (int)(src.Y + ((screenY - dst.Y + 0.5) * src.Height / dst.Height));
                for (var x = 0; x < box.Width; x++)
                {
                    var screenX = box.X + x;
                    if (screenX < dst.X || screenX >= dst.Right)
                    {
                        continue;
                    }

                    var sx = (int)(src.X + ((screenX - dst.X + 0.5) * src.Width / dst.Width));
                    if (sx < 0 || sy < 0 || sx >= planeWidth || sy >= planeHeight)
                    {
                        continue;
                    }

                    var from = ((sy * planeWidth) + sx) * 4;
                    var to = ((((frame * box.Height) + y) * box.Width) + x) * 4;
                    var alpha = layer.Opaque ? 255 : plane[from + 3];
                    for (var c = 0; c < 3; c++)
                    {
                        strip[to + c] = (byte)Math.Min(255, plane[from + c] + (strip[to + c] * (255 - alpha) / 255));
                    }

                    strip[to + 3] = 255;
                }
            }
        }

        private static (double Mean, double Peak) Luma(ReadOnlySpan<byte> rgba)
        {
            Span<int> histogram = stackalloc int[256];
            long sum = 0;
            var count = rgba.Length / 4;
            for (var i = 0; i < rgba.Length; i += 4)
            {
                var luma = ((rgba[i] * 54) + (rgba[i + 1] * 183) + (rgba[i + 2] * 19)) >> 8;
                histogram[luma]++;
                sum += luma;
            }

            if (count == 0)
            {
                return (0, 0);
            }

            var top = Math.Max(1, count / 50);
            long topSum = 0;
            var taken = 0;
            for (var level = 255; level >= 0 && taken < top; level--)
            {
                var take = Math.Min(histogram[level], top - taken);
                topSum += (long)take * level;
                taken += take;
            }

            return ((double)sum / count, (double)topSum / taken);
        }

        private static int Changed(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current)
        {
            var changed = 0;
            for (var i = 0; i < current.Length; i += 4)
            {
                if (Math.Abs(previous[i] - current[i]) > 8 || Math.Abs(previous[i + 1] - current[i + 1]) > 8
                    || Math.Abs(previous[i + 2] - current[i + 2]) > 8)
                {
                    changed++;
                }
            }

            return changed;
        }
    }
}
