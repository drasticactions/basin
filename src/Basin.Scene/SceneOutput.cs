using Pixman;

namespace Basin.Scene;

public sealed class SceneOutput : IDisposable
{
    private readonly Scene _scene;
    private readonly List<Scene.RenderEntry> _renderList = [];
    private readonly List<PixmanRegion32> _regionPool = [];
    private readonly PixmanRegion32 _damage = new();
    private readonly PixmanRegion32 _opaque = new();
    private readonly PixmanRegion32 _scratch = new();
    private readonly PixmanRegion32 _planeScratch = new();
    private readonly PixmanRegion32 _difference = new();
    private Point _position;
    private double _scale;
    private OutputProjection _projection;
    private BufferLock _scanoutHold;
    private int _scanoutStreak;
    private bool _scanningOut;

    private readonly List<SceneBuffer> _candidateNodes = [];
    private readonly List<Box> _candidateBoxes = [];
    private readonly List<FBox> _candidateSrcBoxes = [];
    private readonly List<OutputLayer> _layerPool = [];
    private readonly List<OutputLayer> _layers = [];
    private readonly PixmanRegion32 _compositedAbove = new();
    private List<SceneBuffer> _offloadedNow = [];
    private List<Box> _offloadedNowBoxes = [];
    private List<SceneBuffer> _offloadedPrev = [];
    private List<Box> _offloadedPrevBoxes = [];

    public SceneOutput(Scene scene, IOutput output)
    {
        _scene = scene;
        Output = output;
        _scale = output.Scale;
        _projection = ComputeProjection();
        Ring = new DamageRing(output.CurrentMode.Width, output.CurrentMode.Height);
        scene.Damaged += OnSceneDamaged;
        scene.FrameRequested += OnFrameRequested;
        output.Committed += OnOutputCommitted;
        output.Destroyed += Dispose;
    }

    public IOutput Output { get; }

    public DamageRing Ring { get; }

    private readonly PixmanRegion32 _postDamage = new();

    public Point Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                Ring.AddWhole();
                DamagePending?.Invoke();
            }
        }
    }

    public Box? ReplicationSource
    {
        get => _replicationSource;
        set
        {
            if (_replicationSource == value)
            {
                return;
            }

            _replicationSource = value;
            if (value is { } box)
            {
                _position = new Point(box.X, box.Y);
            }

            _projection = ComputeProjection();
            Ring.AddWhole();
            DamagePending?.Invoke();
        }
    }

    private Box? _replicationSource;

    private OutputProjection ComputeProjection()
    {
        if (_replicationSource is not { } box || box.Width <= 0 || box.Height <= 0)
        {
            return OutputProjection.For(Output);
        }

        var mode = Output.CurrentMode;
        var scale = Math.Min((double)mode.Width / box.Width, (double)mode.Height / box.Height);
        var originX = -(int)Math.Round((mode.Width - box.Width * scale) / 2);
        var originY = -(int)Math.Round((mode.Height - box.Height * scale) / 2);
        return new OutputProjection(scale, OutputTransform.Normal, mode.Width, mode.Height, originX, originY);
    }

    public int ScanoutEntryThreshold { get; set; } = 2;

    public int OffloadEntryThreshold { get; set; } = 3;

    private readonly List<SceneBuffer> _settleNodes = [];
    private readonly List<Box> _settleBoxes = [];
    private readonly List<int> _settleCounts = [];
    private readonly List<int> _settleSeen = [];
    private int _settleEpoch;

    private bool HasSettled(SceneBuffer node, in Box planeBox)
    {
        for (var i = 0; i < _settleNodes.Count; i++)
        {
            if (!ReferenceEquals(_settleNodes[i], node))
            {
                continue;
            }

            _settleSeen[i] = _settleEpoch;
            if (_settleBoxes[i] != planeBox)
            {
                _settleBoxes[i] = planeBox;
                _settleCounts[i] = 1;
                return _settleCounts[i] >= OffloadEntryThreshold;
            }

            if (_settleCounts[i] < OffloadEntryThreshold)
            {
                _settleCounts[i]++;
            }

            return _settleCounts[i] >= OffloadEntryThreshold;
        }

        _settleNodes.Add(node);
        _settleBoxes.Add(planeBox);
        _settleCounts.Add(1);
        _settleSeen.Add(_settleEpoch);
        return OffloadEntryThreshold <= 1;
    }

    private void ForgetUnseenSettles()
    {
        for (var i = _settleNodes.Count - 1; i >= 0; i--)
        {
            if (_settleSeen[i] != _settleEpoch)
            {
                _settleNodes.RemoveAt(i);
                _settleBoxes.RemoveAt(i);
                _settleCounts.RemoveAt(i);
                _settleSeen.RemoveAt(i);
            }
        }
    }

    public bool IsDirectScanout => _scanningOut;

    public long ScanoutCommits { get; private set; }

    public long ComposedCommits { get; private set; }

    public long SkippedCommits { get; private set; }

    public int OffloadedLayers => _offloadedPrev.Count;

    public long OffloadCommits { get; private set; }

    public long PlaneOnlyCommits { get; private set; }

    private readonly long[] _declineCounts = new long[Enum.GetValues<PlaneDeclineReason>().Length];
    private readonly List<SceneNode> _declinedNodes = [];
    private readonly List<PlaneDeclineReason> _declinedReasons = [];

    public long DeclinedFor(PlaneDeclineReason reason) => _declineCounts[(int)reason];

    public IReadOnlyList<SceneNode> DeclinedCandidates => _declinedNodes;

    public IReadOnlyList<PlaneDeclineReason> DeclineReasons => _declinedReasons;

    private void Decline(SceneNode node, PlaneDeclineReason reason)
    {
        _declineCounts[(int)reason]++;
        if (reason is not (PlaneDeclineReason.NotABuffer or PlaneDeclineReason.Mirrored))
        {
            _declinedNodes.Add(node);
            _declinedReasons.Add(reason);
        }
    }

    public event Action<Surface?>? ScanoutCandidateChanged;

    public event Action<IReadOnlyList<SceneBuffer>>? OffloadCandidatesChanged;

    private readonly List<SceneBuffer> _announcedCandidates = [];

    public event Action? DamagePending;

    private BufferLock _softwareCursor;
    private ITexture? _softwareCursorTexture;

    private bool _disposed;
    private int _cursorX, _cursorY, _cursorHotspotX, _cursorHotspotY;

    public void SetSoftwareCursor(IBuffer? image, int hotspotX, int hotspotY)
    {
        DamageCursor();
        _softwareCursorTexture?.Dispose();
        _softwareCursorTexture = null;
        _softwareCursor.Dispose();
        _softwareCursor = image is null ? default : image.Lock();
        _cursorHotspotX = hotspotX;
        _cursorHotspotY = hotspotY;
        DamageCursor();
    }

    public void MoveSoftwareCursor(int x, int y)
    {
        if (x == _cursorX && y == _cursorY)
        {
            return;
        }

        DamageCursor();
        _cursorX = x;
        _cursorY = y;
        DamageCursor();
    }

    private void DamageCursor()
    {
        if (_softwareCursor.Buffer is { } image)
        {
            var wasEmpty = Ring.IsEmpty;
            Ring.Add(_projection.MapPixels(
                new Box(_cursorX - _cursorHotspotX, _cursorY - _cursorHotspotY, image.Width, image.Height)));
            if (wasEmpty && !Ring.IsEmpty)
            {
                DamagePending?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene.Damaged -= OnSceneDamaged;
        _scene.FrameRequested -= OnFrameRequested;
        Output.Committed -= OnOutputCommitted;
        Output.Destroyed -= Dispose;
        _softwareCursorTexture?.Dispose();
        _softwareCursorTexture = null;
        _softwareCursor.Dispose();
        _scanoutHold.Dispose();
        _damage.Dispose();
        _opaque.Dispose();
        _scratch.Dispose();
        _planeScratch.Dispose();
        _difference.Dispose();
        _postDamage.Dispose();
        _compositedAbove.Dispose();
        foreach (var region in _regionPool)
        {
            region.Dispose();
        }

        DropPostBuffers();
        DropFilterScratch();
        Ring.Dispose();
    }

    public bool NeedsRepaint => !Ring.IsEmpty || _scanningOut || _planeContentChanged || _planeCommitRequested;

    private bool _planeContentChanged;
    private bool _planeCommitRequested;

    public void RequestPlaneCommit()
    {
        if (!_planeCommitRequested)
        {
            _planeCommitRequested = true;
            DamagePending?.Invoke();
        }
    }

    private void MarkPlaneContentChanged()
    {
        if (!_planeContentChanged)
        {
            _planeContentChanged = true;
            DamagePending?.Invoke();
        }
    }

    public event Action<FrameTick>? BeforeRepaint;

    private readonly List<IPostStage> _postStages = [];
    private MemoryBuffer? _postA;
    private MemoryBuffer? _postB;
    private ITexture? _postTextureA;
    private ITexture? _postTextureB;
    private IRenderer? _postTextureRenderer;

    public void AddPostStage(IPostStage stage)
    {
        _postStages.Add(stage);
        Ring.AddWhole();
        DamagePending?.Invoke();
    }

    public bool RemovePostStage(IPostStage stage)
    {
        if (!_postStages.Remove(stage))
        {
            return false;
        }

        Ring.AddWhole();
        DamagePending?.Invoke();
        return true;
    }

    private IFrameFilter? _frameFilter;
    private IAllocator? _filterAllocator;
    private IBuffer? _filterScratch;
    private ITexture? _filterTexture;
    private IRenderer? _filterTextureRenderer;
    private ulong _filterFrameCount;
    private long _filterLastPresentNanos;

    public IFrameFilter? FrameFilter => _frameFilter;

    public void SetFrameFilter(IFrameFilter? filter, IAllocator? allocator = null)
    {
        _frameFilter = filter;
        _filterAllocator = filter is null ? null : allocator;
        DropFilterScratch();
        if (_disposed)
        {
            return;
        }

        Ring.AddWhole();
        DamagePending?.Invoke();
    }

    private void DropFilterScratch()
    {
        _filterTexture?.Dispose();
        _filterTexture = null;
        _filterTextureRenderer = null;
        (_filterScratch as BufferBase)?.Destroy();
        _filterScratch = null;
    }

    private IBuffer? EnsureFilterScratch(IRenderer renderer, OutputMode mode)
    {
        if (_filterScratch is { } existing && existing.Width == mode.Width && existing.Height == mode.Height)
        {
            return existing;
        }

        DropFilterScratch();
        if (_filterAllocator is { } allocator)
        {
            var usable = allocator.Formats.Intersect(renderer.DmabufTextureFormats);
            if (usable.Contains(DrmFormat.Xrgb8888))
            {
                ulong[] modifiers = [.. usable.ModifiersOf(DrmFormat.Xrgb8888)];
                _filterScratch = allocator.Allocate(mode.Width, mode.Height, DrmFormat.Xrgb8888, modifiers, BufferUse.Render);
            }
        }

        _filterScratch ??= new MemoryBuffer(mode.Width, mode.Height, DrmFormat.Xrgb8888);
        return _filterScratch;
    }

    private ITexture? FilterTexture(IRenderer renderer, IBuffer scratch)
    {
        if (!ReferenceEquals(_filterTextureRenderer, renderer))
        {
            _filterTexture?.Dispose();
            _filterTexture = null;
            _filterTextureRenderer = renderer;
        }

        return _filterTexture ??= renderer.ImportTexture(scratch);
    }

    private void EnsurePostBuffers(OutputMode mode)
    {
        if (_postA is not null && _postA.Width == mode.Width && _postA.Height == mode.Height)
        {
            return;
        }

        DropPostBuffers();
        _postA = new MemoryBuffer(mode.Width, mode.Height, DrmFormat.Xrgb8888);
        _postB = new MemoryBuffer(mode.Width, mode.Height, DrmFormat.Xrgb8888);
    }

    private ITexture? PostTexture(IRenderer renderer, MemoryBuffer source)
    {
        if (!ReferenceEquals(_postTextureRenderer, renderer))
        {
            DropPostTextures();
            _postTextureRenderer = renderer;
        }

        if (ReferenceEquals(source, _postA))
        {
            _postTextureA ??= renderer.ImportTexture(source);
            return _postTextureA;
        }

        _postTextureB ??= renderer.ImportTexture(source);
        return _postTextureB;
    }

    private void DropPostTextures()
    {
        _postTextureA?.Dispose();
        _postTextureA = null;
        _postTextureB?.Dispose();
        _postTextureB = null;
        _postTextureRenderer = null;
    }

    private void DropPostBuffers()
    {
        DropPostTextures();
        _postA?.Destroy();
        _postA = null;
        _postB?.Destroy();
        _postB = null;
    }

    public bool Commit(IRenderer renderer, Swapchain swapchain, OutputState state, in SceneCommitOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(swapchain);
        return Commit(renderer, swapchain, null, 0, state, options);
    }

    public bool Commit(
        IRenderer renderer, IBuffer target, int age, OutputState state, in SceneCommitOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(age);
        return Commit(renderer, null, target, age, state, options);
    }

    private bool Commit(
        IRenderer renderer,
        Swapchain? swapchain,
        IBuffer? suppliedTarget,
        int suppliedAge,
        OutputState state,
        in SceneCommitOptions options)
    {
        var mode = Output.CurrentMode;
        var tick = new FrameTick(options.TargetPresentNanos, mode.RefreshIntervalNanoseconds);
        _inCommit = true;
        _damagedDuringCommit = false;
        try
        {
            BeforeRepaint?.Invoke(tick);
            return CommitLocked(renderer, swapchain, suppliedTarget, suppliedAge, state, options, mode, tick);
        }
        finally
        {
            _inCommit = false;
            if (_damagedDuringCommit)
            {
                _damagedDuringCommit = false;
                DamagePending?.Invoke();
            }
        }
    }

    private bool _inCommit;
    private bool _damagedDuringCommit;

    private bool CommitLocked(
        IRenderer renderer, Swapchain? swapchain, IBuffer? suppliedTarget, int suppliedAge,
        OutputState state, in SceneCommitOptions options,
        OutputMode mode, in FrameTick tick)
    {

        _scale = Output.Scale;
        _projection = ComputeProjection();
        _runsBackdropEffects = renderer.SupportsBackdropEffects;
        Ring.Resize(mode.Width, mode.Height);
        _renderList.Clear();
        _scene.CollectRenderList(_renderList, -_position.X, -_position.Y);
        _scene.PrepareCaptures(renderer, _renderList, _projection.Scale);

        if (!Ring.IsEmpty && _runsBackdropEffects && AnyActiveBackdrop())
        {
            Ring.AddWhole();
        }

        if (!Ring.IsEmpty && _postStages.Count > 0)
        {
            Ring.GetBufferDamage(1, _postDamage);
            Ring.AddWhole();
        }

        var filter = _frameFilter is { IsSupported: true } installed && renderer.SupportsFrameFilters
            ? installed
            : null;
        if (filter is not null && (filter.NeedsContinuousRepaint || (!Ring.IsEmpty && filter.NeedsFullFrame)))
        {
            Ring.AddWhole();
        }

        if (_postStages.Count == 0 && filter is null && !_projection.IsTransformed && _replicationSource is null &&
            options.AllowDirectScanout && _softwareCursor.Buffer is null && TryDirectScanout(state))
        {
            return true;
        }

        var wasScanningOut = _scanningOut;
        if (wasScanningOut)
        {
            LeaveScanout();
        }

        _offloadedNow.Clear();
        _offloadedNowBoxes.Clear();
        _layers.Clear();
        var sendLayers = false;
        if (options.AllowPlaneOffload && !_projection.IsTransformed && _replicationSource is null &&
            _softwareCursor.Buffer is null && _postStages.Count == 0 && filter is null)
        {
            SelectOverlayCandidates(mode, options.MaxOffloadLayers);
        }
        else
        {
            _candidateNodes.Clear();
            _candidateBoxes.Clear();
            _candidateSrcBoxes.Clear();
            _declinedNodes.Clear();
            _declinedReasons.Clear();
        }

        AnnounceOffloadCandidates();
        if (_candidateNodes.Count > 0)
        {
            OfferLayers(state);
            sendLayers = true;
        }

        if (!sendLayers && _offloadedPrev.Count > 0)
        {
            sendLayers = true;
        }

        DamageOffloadTransitions();

        if (Ring.IsEmpty)
        {
            if (((_planeContentChanged && _offloadedNow.Count > 0) || _planeCommitRequested) &&
                LastTarget is { } unchanged)
            {
                state.Clear();
                state.SetBuffer(unchanged);
                state.SetLayers(_layers);
                if (!Output.Commit(state))
                {
                    return false;
                }

                ReconcileOffloadedLayers();
                _planeContentChanged = false;
                _planeCommitRequested = false;
                PlaneOnlyCommits++;
                OffloadCommits++;
                (_offloadedPrev, _offloadedNow) = (_offloadedNow, _offloadedPrev);
                (_offloadedPrevBoxes, _offloadedNowBoxes) = (_offloadedNowBoxes, _offloadedPrevBoxes);
                return true;
            }

            _planeContentChanged = false;
            _planeCommitRequested = false;
            SkippedCommits++;
            return false;
        }

        var age = suppliedAge;
        var target = suppliedTarget;
        if (swapchain is not null)
        {
            target = swapchain.Acquire(out age);
        }

        if (target is null)
        {
            return false;
        }

        Ring.GetBufferDamage(age, _damage);
        if (filter is not null)
        {
            RenderFiltered(renderer, filter, target, options, mode, tick);
        }
        else if (_postStages.Count > 0)
        {
            RenderWithPost(renderer, target, options, mode, tick);
        }
        else
        {
            Render(renderer, target, options);
        }

        state.Clear();
        state.SetBuffer(target).SetDamage(_damage);
        if (sendLayers)
        {
            state.SetLayers(_layers);
        }

        var completion = Output.SupportsInFence ? renderer.ExportLastSubmissionFence() : -1;
        if (completion >= 0)
        {
            state.SetInFence(completion);
        }

        try
        {
            if (!Output.Commit(state))
            {
                return false;
            }
        }
        finally
        {
            if (completion >= 0)
            {
                RenderFences.CloseFence(completion);
            }
        }

        swapchain?.Presented(target);
        LastTarget = target;
        Ring.Commit();
        ReconcileOffloadedLayers();
        _planeContentChanged = false;
        ComposedCommits++;
        if (_offloadedNow.Count > 0)
        {
            OffloadCommits++;
        }

        (_offloadedPrev, _offloadedNow) = (_offloadedNow, _offloadedPrev);
        (_offloadedPrevBoxes, _offloadedNowBoxes) = (_offloadedNowBoxes, _offloadedPrevBoxes);
        if (filter is { NeedsContinuousRepaint: true })
        {
            _damagedDuringCommit = true;
        }

        return true;
    }

    private void SelectOverlayCandidates(OutputMode mode, int maxLayers)
    {
        _candidateNodes.Clear();
        _candidateBoxes.Clear();
        _candidateSrcBoxes.Clear();
        _declinedNodes.Clear();
        _declinedReasons.Clear();
        _compositedAbove.Clear();
        _settleEpoch++;
        for (var i = _renderList.Count - 1; i >= 0; i--)
        {
            var entry = _renderList[i];
            var bounds = EntryBounds(entry.Node);
            if (bounds.IsEmpty)
            {
                continue;
            }

            var physical = EntryPhysical(entry, bounds);
            if (physical.IsEmpty || physical.X >= mode.Width || physical.Y >= mode.Height || physical.Right <= 0 || physical.Bottom <= 0)
            {
                continue;
            }

            var onScreen = physical.Intersect(new Box(0, 0, mode.Width, mode.Height));
            var planeBox = onScreen;
            var declined = WhyNotAPlane(entry, physical, onScreen, maxLayers, ref planeBox);
            if (declined is null)
            {
                var node = (SceneBuffer)entry.Node;
                _candidateNodes.Add(node);
                _candidateBoxes.Add(planeBox);
                _candidateSrcBoxes.Add(CropSource(node, physical, planeBox));
            }
            else
            {
                Decline(entry.Node, declined.Value);
                _scratch.Reset(new PixmanBox32(onScreen.X, onScreen.Y, onScreen.Right, onScreen.Bottom));
                _compositedAbove.UnionWith(_scratch);
            }
        }

        ForgetUnseenSettles();
    }

    private PlaneDeclineReason? WhyNotAPlane(
        in Scene.RenderEntry entry, in Box physical, in Box onScreen, int maxLayers, ref Box planeBox)
    {
        if (entry.Mirrored)
        {
            return PlaneDeclineReason.Mirrored;
        }

        if (entry.Node is not SceneBuffer node)
        {
            return PlaneDeclineReason.NotABuffer;
        }

        if (node.Lut is not null)
        {
            return PlaneDeclineReason.ColorTransform;
        }

        if (node.HasActiveBackdrop && _runsBackdropEffects)
        {
            return PlaneDeclineReason.BackdropEffect;
        }

        if (node.TextureShader is not null)
        {
            return PlaneDeclineReason.PixelShader;
        }

        if (entry.Transformed || entry.Alpha < 1f)
        {
            return PlaneDeclineReason.Transformed;
        }

        if (node.Buffer is not { } content || !content.TryGetDmabuf(out var attributes))
        {
            return PlaneDeclineReason.NoDmabuf;
        }

        if (attributes.Modifier == DrmFormatSet.ModifierInvalid)
        {
            return PlaneDeclineReason.ImplicitModifier;
        }

        if (!Output.CanScanout(attributes.Format, attributes.Modifier, overlay: true))
        {
            return PlaneDeclineReason.UnscannableLayout;
        }

        if (!CoversWholeNode(entry, physical))
        {
            return PlaneDeclineReason.Clipped;
        }

        if (onScreen.IsEmpty)
        {
            return PlaneDeclineReason.OffOutput;
        }

        if (_candidateNodes.Count >= maxLayers)
        {
            return PlaneDeclineReason.LayerBudget;
        }

        _scratch.Reset(new PixmanBox32(onScreen.X, onScreen.Y, onScreen.Right, onScreen.Bottom));
        _scratch.SubtractWith(_compositedAbove);
        if (_scratch.RectangleCount != 1)
        {
            return PlaneDeclineReason.CoveredFromAbove;
        }

        var free = _scratch.Extents;
        planeBox = new Box(free.X1, free.Y1, free.X2 - free.X1, free.Y2 - free.Y1);
        return HasSettled(node, planeBox) ? null : PlaneDeclineReason.Settling;
    }

    private static FBox CropSource(SceneBuffer node, in Box physical, in Box onScreen)
    {
        if (onScreen == physical)
        {
            return node.SourceBox;
        }

        var source = node.SourceBox.IsEmpty
            ? new FBox(0, 0, node.Buffer!.Width, node.Buffer.Height)
            : node.SourceBox;
        return new FBox(
            source.X + (onScreen.X - physical.X) * source.Width / physical.Width,
            source.Y + (onScreen.Y - physical.Y) * source.Height / physical.Height,
            onScreen.Width * source.Width / physical.Width,
            onScreen.Height * source.Height / physical.Height);
    }

    private void OfferLayers(OutputState state)
    {
        while (_layerPool.Count < _candidateNodes.Count)
        {
            _layerPool.Add(new OutputLayer());
        }

        for (var i = _candidateNodes.Count - 1; i >= 0; i--)
        {
            var node = _candidateNodes[i];
            var layer = _layerPool[_layers.Count];
            layer.Buffer = node.Buffer;
            layer.SrcBox = _candidateSrcBoxes[i];
            layer.DstBox = _candidateBoxes[i];
            layer.InFenceFd = node.AcquireFenceFd;
            layer.Alpha = 1f;
            layer.Opaque = node.IsOpaque;
            layer.Accepted = false;
            _layers.Add(layer);
        }

        state.Clear();
        state.SetLayers(_layers);
        _ = Output.TestCommit(state);

        _compositedAbove.Clear();
        for (var i = 0; i < _candidateNodes.Count; i++)
        {
            var layer = _layers[_layers.Count - 1 - i];
            var box = _candidateBoxes[i];
            var offload = layer.Accepted;
            if (offload)
            {
                _scratch.Reset(new PixmanBox32(box.X, box.Y, box.Right, box.Bottom));
                _scratch.IntersectWith(_compositedAbove);
                offload = _scratch.IsEmpty;
            }

            if (offload)
            {
                _offloadedNow.Add(_candidateNodes[i]);
                _offloadedNowBoxes.Add(box);
            }
            else
            {
                Decline(_candidateNodes[i], layer.Accepted ? PlaneDeclineReason.Demoted : PlaneDeclineReason.BackendRefused);
                _scratch.Reset(new PixmanBox32(box.X, box.Y, box.Right, box.Bottom));
                _compositedAbove.UnionWith(_scratch);
                layer.Buffer = null;
            }
        }

        _layers.RemoveAll(static l => l.Buffer is null);
    }

    private void DamageOffloadTransitions()
    {
        for (var i = 0; i < _offloadedPrev.Count; i++)
        {
            var index = IndexOfNode(_offloadedNow, _offloadedPrev[i]);
            if (index < 0)
            {
                Ring.Add(_offloadedPrevBoxes[i]);
            }
            else if (_offloadedNowBoxes[index] != _offloadedPrevBoxes[i])
            {
                DamageBetween(_offloadedPrevBoxes[i], _offloadedNowBoxes[index]);
            }
        }

        for (var i = 0; i < _offloadedNow.Count; i++)
        {
            if (IndexOfNode(_offloadedPrev, _offloadedNow[i]) < 0)
            {
                Ring.Add(_offloadedNowBoxes[i]);
            }
        }
    }

    private void ReconcileOffloadedLayers()
    {
        var demoted = false;
        for (var i = _offloadedNow.Count - 1; i >= 0; i--)
        {
            if (_layers[_layers.Count - 1 - i].Accepted)
            {
                continue;
            }

            Ring.Add(_offloadedNowBoxes[i]);
            _offloadedNow.RemoveAt(i);
            _offloadedNowBoxes.RemoveAt(i);
            demoted = true;
        }

        if (demoted)
        {
            DamagePending?.Invoke();
        }
    }

    private void DamageBetween(in Box before, in Box after)
    {
        _scratch.Reset(new PixmanBox32(before.X, before.Y, before.Right, before.Bottom));
        _planeScratch.Reset(new PixmanBox32(after.X, after.Y, after.Right, after.Bottom));
        _difference.Copy(_scratch);
        _difference.SubtractWith(_planeScratch);
        Ring.Add(_difference);
        _difference.Copy(_planeScratch);
        _difference.SubtractWith(_scratch);
        Ring.Add(_difference);
    }

    private void AnnounceOffloadCandidates()
    {
        var changed = _announcedCandidates.Count != _candidateNodes.Count;
        for (var i = 0; !changed && i < _candidateNodes.Count; i++)
        {
            changed = !ReferenceEquals(_announcedCandidates[i], _candidateNodes[i]);
        }

        if (!changed)
        {
            return;
        }

        _announcedCandidates.Clear();
        _announcedCandidates.AddRange(_candidateNodes);
        OffloadCandidatesChanged?.Invoke(_announcedCandidates);
    }

    private static int IndexOfNode(List<SceneBuffer> list, SceneBuffer node)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool ContainsNode(List<SceneBuffer> list, SceneBuffer node)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], node))
            {
                return true;
            }
        }

        return false;
    }

    private Box? OffloadedBoxOf(SceneNode node)
    {
        if (node is not SceneBuffer buffer)
        {
            return null;
        }

        var index = IndexOfNode(_offloadedNow, buffer);
        return index < 0 ? null : _offloadedNowBoxes[index];
    }

    public IBuffer? LastTarget { get; private set; }

    private int GatherSampledFences(out bool owned)
    {
        var fence = -1;
        owned = false;
        for (var i = 0; i < _renderList.Count; i++)
        {
            if (_renderList[i].Node is SceneBuffer offloaded &&
                _offloadedNow.Count > 0 && IndexOfNode(_offloadedNow, offloaded) >= 0)
            {
                continue;
            }

            if (fence < 0 && _renderList[i].Node is SceneBuffer { AcquireFenceFd: >= 0 } first)
            {
                fence = first.AcquireFenceFd;
                continue;
            }

            AccumulateEntryFence(_renderList[i].Node, ref fence, ref owned);
        }

        return fence;
    }

    private static void AccumulateEntryFence(SceneNode node, ref int fence, ref bool owned)
    {
        switch (node)
        {
            case SceneBuffer { AcquireFenceFd: >= 0 } buffer:
                if (fence < 0)
                {
                    fence = buffer.AcquireFenceFd;
                    return;
                }

                var merged = RenderFences.MergeSyncFiles(fence, buffer.AcquireFenceFd);
                if (merged < 0)
                {
                    return;
                }

                if (owned)
                {
                    _ = CloseFd(fence);
                }

                fence = merged;
                owned = true;
                return;

            case SceneTree tree:
                foreach (var child in tree.Children)
                {
                    if (child.Enabled)
                    {
                        AccumulateEntryFence(child, ref fence, ref owned);
                    }
                }

                return;
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "close")]
    private static extern int CloseFd(int fd);

    private void RenderWithPost(
        IRenderer renderer, IBuffer target, in SceneCommitOptions options, OutputMode mode, in FrameTick tick,
        bool drawCursor = true)
    {
        EnsurePostBuffers(mode);
        Render(renderer, _postA!, options, drawCursor: false);

        MemoryBuffer source = _postA!;
        for (var i = 0; i < _postStages.Count; i++)
        {
            var last = i == _postStages.Count - 1;
            var stageTarget = last ? target : ReferenceEquals(source, _postA) ? _postB! : _postA!;
            var texture = PostTexture(renderer, source);
            if (texture is null)
            {
                Render(renderer, target, options, drawCursor);
                return;
            }

            (texture as IRefreshableTexture)?.MarkDirty();
            var pass = renderer.BeginBufferPass(stageTarget, new RenderPassOptions());
            _postStages[i].Render(pass, texture, new PostContext(mode.Width, mode.Height, tick, _postDamage));
            if (last && drawCursor)
            {
                DrawSoftwareCursor(renderer, pass);
            }

            pass.Submit();
            if (stageTarget is MemoryBuffer next && !last)
            {
                source = next;
            }
        }
    }

    private void RenderFiltered(
        IRenderer renderer, IFrameFilter filter, IBuffer target, in SceneCommitOptions options,
        OutputMode mode, in FrameTick tick)
    {
        var scratch = EnsureFilterScratch(renderer, mode);
        var texture = scratch is null ? null : FilterTexture(renderer, scratch);
        if (scratch is null || texture is null)
        {
            if (_postStages.Count > 0)
            {
                RenderWithPost(renderer, target, options, mode, tick);
            }
            else
            {
                Render(renderer, target, options);
            }

            return;
        }

        if (_postStages.Count > 0)
        {
            RenderWithPost(renderer, scratch, options, mode, tick, drawCursor: false);
        }
        else
        {
            Render(renderer, scratch, options, drawCursor: false);
        }

        (texture as IRefreshableTexture)?.MarkDirty();
        _filterFrameCount++;
        var delta = _filterLastPresentNanos > 0 && tick.TargetPresentNanos > _filterLastPresentNanos
            ? (uint)Math.Min((tick.TargetPresentNanos - _filterLastPresentNanos) / 1_000_000, 1000)
            : 0u;
        _filterLastPresentNanos = tick.TargetPresentNanos;
        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        var recorded = pass.AddFrameFilter(filter, texture, new FrameFilterOptions
        {
            FrameCount = _filterFrameCount,
            FramesPerSecond = tick.RefreshIntervalNanos > 0
                ? (float)(1_000_000_000.0 / tick.RefreshIntervalNanos)
                : 0f,
            FrametimeDeltaMillis = delta,
            Rotation = RotationOf(Output.Transform),
        });
        if (!recorded)
        {
            pass.AddTexture(texture, new TextureRenderOptions
            {
                DstBox = new Box(0, 0, mode.Width, mode.Height),
                Opaque = true,
            });
        }

        DrawSoftwareCursor(renderer, pass);
        pass.Submit();
    }

    private static uint RotationOf(OutputTransform transform) => transform switch
    {
        OutputTransform.Rotate90 or OutputTransform.Flipped90 => 1,
        OutputTransform.Rotate180 or OutputTransform.Flipped180 => 2,
        OutputTransform.Rotate270 or OutputTransform.Flipped270 => 3,
        _ => 0,
    };

    private void DrawSoftwareCursor(IRenderer renderer, IRenderPass pass)
    {
        if (_softwareCursor.Buffer is { } cursorImage &&
            (_softwareCursorTexture ??= renderer.ImportTexture(cursorImage)) is { } cursorTexture)
        {
            pass.AddTexture(cursorTexture, new TextureRenderOptions
            {
                DstBox = new Box(_cursorX - _cursorHotspotX, _cursorY - _cursorHotspotY, cursorImage.Width, cursorImage.Height),
                Transform = _projection.MapsPixels ? _projection.Matrix : RenderTransform.Identity,
                Clip = _damage,
            });
        }
    }

    private void Render(IRenderer renderer, IBuffer target, in SceneCommitOptions options, bool drawCursor = true)
    {
        var waitFence = GatherSampledFences(out var ownsFence);

        _opaque.Clear();
        EnsurePool(_renderList.Count + 1);
        var anyBackground = PoolRegion(0);
        anyBackground.Copy(_damage);
        for (var i = _renderList.Count - 1; i >= 0; i--)
        {
            var entry = _renderList[i];
            var clip = PoolRegion(i + 1);
            var bounds = EntryBounds(entry.Node);
            if (bounds.IsEmpty)
            {
                clip.Clear();
                continue;
            }

            var onPlane = !entry.Mirrored && _offloadedNow.Count > 0 ? OffloadedBoxOf(entry.Node) : null;
            var physical = EntryPhysical(entry, bounds);
            if (physical.IsEmpty)
            {
                clip.Clear();
                continue;
            }

            if (entry.Clip is { } clipBox)
            {
                physical = physical.Intersect(_projection.Project(clipBox));
                if (physical.IsEmpty)
                {
                    clip.Clear();
                    continue;
                }
            }

            clip.Reset(new PixmanBox32(physical.X, physical.Y, physical.Right, physical.Bottom));
            clip.IntersectWith(_damage);
            if (onPlane is { } planeBox)
            {
                _planeScratch.Reset(new PixmanBox32(planeBox.X, planeBox.Y, planeBox.Right, planeBox.Bottom));
                clip.SubtractWith(_planeScratch);
            }

            clip.SubtractWith(_opaque);

            if (!clip.IsEmpty && !entry.Transformed && !entry.Mirrored && entry.Alpha >= 1f && IsOpaqueEntry(entry.Node))
            {
                _scratch.Reset(new PixmanBox32(physical.X, physical.Y, physical.Right, physical.Bottom));
                if (onPlane is not null)
                {
                    _scratch.SubtractWith(_planeScratch);
                }

                _opaque.UnionWith(_scratch);
                anyBackground.SubtractWith(_scratch);
            }
            else if (!clip.IsEmpty && !entry.Transformed && !entry.Mirrored && entry.Alpha >= 1f &&
                _projection.Scale == 1.0 && !_projection.IsTransformed &&
                entry.Node is SceneBuffer { TextureShader: null } partialNode &&
                !(partialNode.HasActiveBackdrop && _runsBackdropEffects) &&
                partialNode.OpaqueRegion is { } partial)
            {
                _scratch.Copy(partial);
                _scratch.Translate(physical.X - bounds.X, physical.Y - bounds.Y);
                _scratch.IntersectRect(_scratch, physical.X, physical.Y, (uint)physical.Width, (uint)physical.Height);
                if (onPlane is not null)
                {
                    _scratch.SubtractWith(_planeScratch);
                }

                _opaque.UnionWith(_scratch);
                anyBackground.SubtractWith(_scratch);
            }
        }

        var pass = renderer.BeginBufferPass(target, new RenderPassOptions { WaitFenceFd = waitFence });
        if (ownsFence)
        {
            _ = CloseFd(waitFence);
        }

        if (!anyBackground.IsEmpty && options.Background.A > 0)
        {
            var extents = anyBackground.Extents;
            pass.AddRect(options.Background, new Box(extents.X1, extents.Y1, extents.X2 - extents.X1, extents.Y2 - extents.Y1), anyBackground);
        }

        for (var i = 0; i < _renderList.Count; i++)
        {
            var clip = PoolRegion(i + 1);
            if (!clip.IsEmpty)
            {
                Scene.DrawEntry(renderer, pass, _renderList[i], clip, _projection);
            }
        }

        if (drawCursor)
        {
            DrawSoftwareCursor(renderer, pass);
        }

        if (options.DebugDamageTint)
        {
            var extents = _damage.Extents;
            pass.AddRect(new RenderColor(0.35f, 0.05f, 0.05f, 0.35f), new Box(extents.X1, extents.Y1, extents.X2 - extents.X1, extents.Y2 - extents.Y1), _damage);
        }

        pass.Submit();
    }

    private bool CoversWholeNode(in Scene.RenderEntry entry, in Box physical) =>
        entry.Clip is not { } clipBox ||
        _projection.Project(clipBox).Contains(physical);

    private Box EntryPhysical(in Scene.RenderEntry entry, in Box bounds)
    {
        var logical = bounds.Translated(entry.X, entry.Y);
        if (!entry.Transformed)
        {
            return _projection.Project(logical);
        }

        if (!entry.Transform.TryMapBounds(logical, out var hull))
        {
            return default;
        }

        var physical = _projection.Project(hull);
        return new Box(physical.X - 1, physical.Y - 1, physical.Width + 2, physical.Height + 2);
    }

    private static (int Width, int Height) EntrySize(SceneNode node) => node switch
    {
        SceneRect rect => (rect.Width, rect.Height),
        SceneBuffer buffer => buffer.Size,
        _ => (0, 0),
    };

    private static Box EntryBounds(SceneNode node) => node switch
    {
        SceneRect rect => new Box(0, 0, rect.Width, rect.Height),
        SceneBuffer buffer => BufferBounds(buffer),
        SceneMesh mesh => mesh.SubtreeBounds(),
        SceneShader shader => shader.SubtreeBounds(),
        SceneTransform transform => transform.SubtreeBounds(),
        _ => default,
    };

    private static Box BufferBounds(SceneBuffer buffer)
    {
        var (width, height) = buffer.Size;
        return new Box(0, 0, width, height);
    }

    private bool _runsBackdropEffects;

    private bool IsOpaqueEntry(SceneNode node) => node switch
    {
        SceneRect rect => rect.IsOpaque,
        SceneBuffer buffer => buffer.IsOpaque && buffer.TextureShader is null && !(buffer.HasActiveBackdrop && _runsBackdropEffects),
        _ => false,
    };

    private bool AnyActiveBackdrop()
    {
        for (var i = 0; i < _renderList.Count; i++)
        {
            switch (_renderList[i].Node)
            {
                case SceneBuffer { HasActiveBackdrop: true }:
                    return true;

                case SceneTransform { Deformer: not null } node when SubtreeHasBackdrop(node):
                    return true;
            }
        }

        return false;
    }

    private static bool SubtreeHasBackdrop(SceneTree tree)
    {
        foreach (var child in tree.Children)
        {
            if (!child.Enabled)
            {
                continue;
            }

            if (child is SceneBuffer { HasActiveBackdrop: true })
            {
                return true;
            }

            if (child is SceneTree subtree && SubtreeHasBackdrop(subtree))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsurePool(int count)
    {
        while (_regionPool.Count < count)
        {
            _regionPool.Add(new PixmanRegion32());
        }
    }

    private PixmanRegion32 PoolRegion(int index) => _regionPool[index];

    private void OnOutputCommitted(OutputStateFields fields)
    {
        var mode = Output.CurrentMode;
        if (mode.Width != Ring.Width || mode.Height != Ring.Height)
        {
            Ring.Resize(mode.Width, mode.Height);
            DamagePending?.Invoke();
        }

        if (Output.Scale != _scale ||
            (fields & OutputStateFields.AspectRatio) != 0 ||
            (_replicationSource is null && Output.Transform != _projection.Transform))
        {
            _scale = Output.Scale;
            _projection = ComputeProjection();
            Ring.AddWhole();
            DamagePending?.Invoke();
        }
    }

    private bool TryDirectScanout(OutputState state)
    {
        var candidate = FindScanoutCandidate();
        if (candidate is null)
        {
            if (_scanoutStreak != 0 || _scanningOut)
            {
                ScanoutCandidateChanged?.Invoke(null);
            }

            _scanoutStreak = 0;
            return false;
        }

        var buffer = candidate.Buffer!;
        if (_scanoutStreak == 0)
        {
            ScanoutCandidateChanged?.Invoke(candidate.InputSurface);
        }

        _scanoutStreak++;
        if (!_scanningOut && _scanoutStreak < ScanoutEntryThreshold)
        {
            return false;
        }

        state.Clear();
        state.SetBuffer(buffer);
        if (candidate.AcquireFenceFd >= 0)
        {
            state.SetInFence(candidate.AcquireFenceFd);
        }

        if (_offloadedPrev.Count > 0)
        {
            _layers.Clear();
            state.SetLayers(_layers);
        }

        if ((!_scanningOut && !Output.TestCommit(state)) || !Output.Commit(state))
        {
            _scanoutStreak = 0;
            if (_scanningOut)
            {
                LeaveScanout();
            }

            return false;
        }

        _scanoutHold.Dispose();
        _scanoutHold = buffer.Lock();
        _scanningOut = true;
        _offloadedPrev.Clear();
        _offloadedPrevBoxes.Clear();
        ScanoutCommits++;
        Ring.Commit();
        return true;
    }

    private void LeaveScanout()
    {
        _scanningOut = false;
        _scanoutStreak = 0;
        _scanoutHold.Dispose();
        _scanoutHold = default;
        Ring.AddWhole();
    }

    private SceneBuffer? FindScanoutCandidate()
    {
        var mode = Output.CurrentMode;
        for (var i = _renderList.Count - 1; i >= 0; i--)
        {
            var entry = _renderList[i];
            if (ExcludedFromScanout(entry.Node))
            {
                continue;
            }

            var bounds = EntryBounds(entry.Node);
            if (bounds.IsEmpty)
            {
                continue;
            }

            var physical = EntryPhysical(entry, bounds);
            var intersects = !physical.IsEmpty &&
                physical.X < mode.Width && physical.Y < mode.Height && physical.Right > 0 && physical.Bottom > 0;
            if (!intersects)
            {
                continue;
            }

            if (!entry.Transformed && !entry.Mirrored && entry.Alpha >= 1f &&
                entry.Node is SceneBuffer { Lut: null, TextureShader: null } buffer &&
                !(buffer.HasActiveBackdrop && _runsBackdropEffects) &&
                CoversWholeNode(entry, physical) &&
                buffer.IsOpaque &&
                physical.X == 0 && physical.Y == 0 &&
                physical.Width == mode.Width && physical.Height == mode.Height &&
                buffer.SourceBox == default &&
                buffer.Buffer is { } content &&
                content.Width == mode.Width && content.Height == mode.Height &&
                content.TryGetDmabuf(out var scanoutAttributes) &&
                Output.CanScanout(scanoutAttributes.Format, scanoutAttributes.Modifier, overlay: false) &&
                (buffer.AcquireFenceFd < 0 || Output.SupportsInFence))
            {
                return buffer;
            }

            return null;
        }

        return null;
    }

    private static bool ExcludedFromScanout(SceneNode node)
    {
        for (var tree = node as SceneTree ?? node.Parent; tree is not null; tree = tree.Parent)
        {
            if (tree.ExcludeFromScanout)
            {
                return true;
            }
        }

        return false;
    }

    private void OnSceneDamaged(SceneNode? source, Box box)
    {
        var local = _projection.ProjectExpanded(
            new Box(box.X - _position.X, box.Y - _position.Y, box.Width, box.Height));
        if (local.X >= Ring.Width || local.Y >= Ring.Height || local.Right <= 0 || local.Bottom <= 0)
        {
            return;
        }

        if (_inCommit)
        {
            _damagedDuringCommit = true;
        }

        if (source is SceneBuffer node && _offloadedPrev.Count > 0 &&
            IndexOfNode(_offloadedPrev, node) is var index && index >= 0)
        {
            _planeScratch.Reset(new PixmanBox32(local.X, local.Y, local.Right, local.Bottom));
            var shown = _offloadedPrevBoxes[index];
            _difference.Reset(new PixmanBox32(shown.X, shown.Y, shown.Right, shown.Bottom));
            _planeScratch.SubtractWith(_difference);
            MarkPlaneContentChanged();
            if (_planeScratch.IsEmpty)
            {
                return;
            }

            var ringWasEmpty = Ring.IsEmpty;
            Ring.Add(_planeScratch);
            if (ringWasEmpty && !Ring.IsEmpty)
            {
                DamagePending?.Invoke();
            }

            return;
        }

        var wasEmpty = Ring.IsEmpty;
        Ring.Add(local);

        if (wasEmpty && !Ring.IsEmpty)
        {
            DamagePending?.Invoke();
        }
    }

    private void OnFrameRequested() => DamagePending?.Invoke();
}
