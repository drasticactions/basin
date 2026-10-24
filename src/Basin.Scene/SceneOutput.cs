using Basin.Capabilities;
using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// Draws a <see cref="Scene"/> onto one <see cref="IOutput"/> and commits only the damaged parts.
/// It also picks direct scanout and overlay planes, and it disposes itself when the output is destroyed.
/// </summary>
public sealed class SceneOutput : IDisposable, IColorLutTable
{
    private readonly Scene _scene;
    private readonly Dictionary<ImageDescription, IColorLut?> _luts = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ImageDescription> _descriptionScratch = [];
    private IColorLut? _defaultLut;
    private ImageDescription? _colorDescription;
    private IColorTransformResolver? _resolver;
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
    private readonly List<OutputLayer> _offloadedNowLayers = [];
    private List<SceneBuffer> _offloadedPrev = [];
    private List<Box> _offloadedPrevBoxes = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneOutput"/> class.
    /// It attaches to the scene and subscribes to the damage and commit events of both objects.
    /// </summary>
    /// <param name="scene">The scene to draw.</param>
    /// <param name="output">The output that receives the frames.</param>
    public SceneOutput(Scene scene, IOutput output)
    {
        _scene = scene;
        Output = output;
        _scale = output.Scale;
        _projection = ComputeProjection();
        Ring = new DamageRing(output.CurrentMode.Width, output.CurrentMode.Height);
        scene.Damaged += OnSceneDamaged;
        scene.FrameRequested += OnFrameRequested;
        scene.ColorDescriptionChanged += OnColorDescriptionChanged;
        output.Committed += OnOutputCommitted;
        output.Destroyed += Dispose;
        scene.Attach(this);
        RebuildLuts();
    }

    /// <summary>
    /// Gets the output that this object draws to.
    /// </summary>
    public IOutput Output { get; }

    /// <summary>
    /// Gets or sets the color space of the output. <see langword="null"/> means the default SDR description.
    /// A change in content rebuilds the color lookup tables and damages the whole output.
    /// </summary>
    public ImageDescription? ColorDescription
    {
        get => _colorDescription;
        set
        {
            if (ReferenceEquals(_colorDescription, value) ||
                (_colorDescription is not null && value is not null &&
                 ImageDescription.ContentComparer.Equals(_colorDescription, value)))
            {
                _colorDescription = value;
                return;
            }

            _colorDescription = value;
            RebuildLuts();
        }
    }

    /// <summary>
    /// Gets or sets the resolver that makes the color lookup tables. When it is <see langword="null"/>, the getter returns <see cref="Scene.ColorTransforms"/> of the scene.
    /// A new value rebuilds the tables.
    /// </summary>
    public IColorTransformResolver? Resolver
    {
        get => _resolver ?? _scene.ColorTransforms;
        set
        {
            if (ReferenceEquals(_resolver, value))
            {
                return;
            }

            _resolver = value;
            RebuildLuts();
        }
    }

    /// <summary>
    /// Gets the number of color lookup tables that this output holds now. A buffer with no transform adds no table.
    /// </summary>
    public int LutCount
    {
        get
        {
            var count = _defaultLut is null ? 0 : 1;
            foreach (var lut in _luts.Values)
            {
                count += lut is null ? 0 : 1;
            }

            return count;
        }
    }

    /// <summary>
    /// Gets the color lookup table that converts the content of a buffer node to the color space of this output.
    /// </summary>
    /// <param name="node">The buffer node to draw.</param>
    /// <returns>The table for the color description of the node, or <see langword="null"/> when no conversion is necessary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public IColorLut? LutFor(SceneBuffer node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.ColorDescription is { } description
            ? _luts.GetValueOrDefault(description)
            : _defaultLut;
    }

    /// <summary>
    /// Makes the color lookup tables again for every color description in the scene.
    /// When a table existed before or exists after, it damages the whole output and raises <see cref="DamagePending"/>.
    /// </summary>
    public void RebuildLuts()
    {
        var resolver = Resolver;
        var output = _colorDescription ?? ImageDescription.SdrDefault;
        var before = LutCount;
        _luts.Clear();
        _defaultLut = resolver?.Resolve(ImageDescription.SdrDefault, output);
        _descriptionScratch.Clear();
        _scene.CollectColorDescriptions(_descriptionScratch);
        foreach (var description in _descriptionScratch)
        {
            _luts[description] = resolver?.Resolve(description, output);
        }

        _descriptionScratch.Clear();
        if (before != 0 || LutCount != 0)
        {
            Ring.AddWhole();
            DamagePending?.Invoke();
        }
    }

    private void OnColorDescriptionChanged(SceneBuffer node)
    {
        if (node.ColorDescription is { } description && !_luts.ContainsKey(description))
        {
            _luts[description] = Resolver?.Resolve(description, _colorDescription ?? ImageDescription.SdrDefault);
        }
    }

    /// <summary>
    /// Gets the damage history of the output in framebuffer pixels. A caller can add damage to it to force a repaint.
    /// </summary>
    public DamageRing Ring { get; }

    private readonly PixmanRegion32 _postDamage = new();

    /// <summary>
    /// Gets or sets the position of the top-left corner of the output in layout coordinates.
    /// A change damages the whole output and raises <see cref="DamagePending"/>.
    /// </summary>
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

    /// <summary>
    /// Gets the map from output-local logical coordinates to framebuffer pixels. The last commit or a scale, transform or replication change updates it.
    /// </summary>
    public OutputProjection Projection => _projection;

    /// <summary>
    /// Gets or sets a layout box that this output shows scaled to fit its mode, with the aspect ratio kept.
    /// <see langword="null"/> stops replication. A box also moves <see cref="Position"/> to its corner.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the number of commits in a row that must find the same scanout candidate before direct scanout starts. The default is 2.
    /// </summary>
    public int ScanoutEntryThreshold { get; set; } = 2;

    private const double ScaledSlack = 1e-3;

    /// <summary>
    /// Gets or sets the number of commits in a row that a buffer must keep the same plane box before it goes to an overlay plane. The default is 3.
    /// </summary>
    public int OffloadEntryThreshold { get; set; } = 3;

    private readonly List<SceneBuffer> _settleNodes = [];
    private readonly List<Box> _settleBoxes = [];
    private readonly List<int> _settleCounts = [];
    private readonly List<int> _settleSeen = [];
    private readonly List<PlaneRefusal?> _settleRefusals = [];
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
                _settleRefusals[i] = null;
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
        _settleRefusals.Add(null);
        return OffloadEntryThreshold <= 1;
    }

    private bool WasRefused(SceneBuffer node, in PlaneRefusal layer)
    {
        for (var i = 0; i < _settleNodes.Count; i++)
        {
            if (ReferenceEquals(_settleNodes[i], node))
            {
                return _settleRefusals[i] == layer;
            }
        }

        return false;
    }

    private void RememberRefusal(SceneBuffer node, in Box planeBox)
    {
        if (node.Buffer is not { } content || !content.TryGetDmabuf(out var attributes))
        {
            return;
        }

        for (var i = 0; i < _settleNodes.Count; i++)
        {
            if (ReferenceEquals(_settleNodes[i], node))
            {
                _settleRefusals[i] = new PlaneRefusal(
                    content.Width, content.Height, planeBox.Width, planeBox.Height, attributes.Format, attributes.Modifier,
                    Output.PlaneEpoch);
                return;
            }
        }
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
                _settleRefusals.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the last commit put a client buffer directly on the primary plane, with no composition.
    /// </summary>
    public bool IsDirectScanout => _scanningOut;

    /// <summary>
    /// Gets or sets a value indicating whether direct scanout sends the HDR metadata of the client content instead of <see cref="OutputHdrMetadata"/>.
    /// It has no effect while <see cref="OutputHdrMetadata"/> is <see langword="null"/>.
    /// </summary>
    public bool HdrMetadataPassthrough { get; set; }

    /// <summary>
    /// Gets or sets the HDR metadata that a composed frame sends to the output. <see langword="null"/> sends none.
    /// </summary>
    public HdrStaticMetadata? OutputHdrMetadata { get; set; }

    /// <summary>
    /// Gets the client HDR metadata that the output shows now through direct scanout. It is <see langword="null"/> after a composed commit.
    /// </summary>
    public HdrStaticMetadata? ContentHdrMetadata => _contentHdr;

    /// <summary>
    /// Gets or sets the source of content-type and YUV hints for client surfaces. <see langword="null"/> gives no hints.
    /// </summary>
    public Basin.Capabilities.ISurfaceHints? SurfaceHints { get; set; }

    /// <summary>
    /// Gets or sets a content type that every commit sends. <see langword="null"/> uses the hint of the scanned-out surface, or none for a composed frame.
    /// </summary>
    public OutputContentType? ContentTypeOverride { get; set; }

    /// <summary>
    /// Gets the content type that the last successful commit sent to the output.
    /// </summary>
    public OutputContentType ContentType => _contentType;

    private OutputContentType _contentType;

    /// <summary>
    /// Converts the content type that a client set on its surface to the content type of an output.
    /// </summary>
    /// <param name="type">The content type of the surface.</param>
    /// <returns>The matching output content type, or <see cref="OutputContentType.None"/> for an unknown value.</returns>
    public static OutputContentType ContentTypeFor(Basin.Capabilities.SurfaceContentType type) => type switch
    {
        Basin.Capabilities.SurfaceContentType.Photo => OutputContentType.Photo,
        Basin.Capabilities.SurfaceContentType.Video => OutputContentType.Cinema,
        Basin.Capabilities.SurfaceContentType.Game => OutputContentType.Game,
        _ => OutputContentType.None,
    };

    private HdrStaticMetadata? _contentHdr;

    /// <summary>
    /// Gets the number of successful commits that used direct scanout.
    /// </summary>
    public long ScanoutCommits { get; private set; }

    /// <summary>
    /// Gets the number of successful commits that drew a composed frame.
    /// </summary>
    public long ComposedCommits { get; private set; }

    /// <summary>
    /// Gets the number of commit calls that found no damage and sent nothing to the output.
    /// </summary>
    public long SkippedCommits { get; private set; }

    /// <summary>
    /// Gets the number of buffers that the last commit put on overlay planes.
    /// </summary>
    public int OffloadedLayers => _offloadedPrev.Count;

    /// <summary>
    /// Gets the number of successful commits that put at least one buffer on an overlay plane.
    /// </summary>
    public long OffloadCommits { get; private set; }

    /// <summary>
    /// Gets the number of commits that changed only overlay planes and sent the previous composed frame again.
    /// </summary>
    public long PlaneOnlyCommits { get; private set; }

    private readonly long[] _declineCounts = new long[Enum.GetValues<PlaneDeclineReason>().Length];
    private readonly List<SceneNode> _declinedNodes = [];
    private readonly List<PlaneDeclineReason> _declinedReasons = [];

    /// <summary>
    /// Gets the number of times that a candidate did not go to a plane for one reason, since this object was made.
    /// </summary>
    /// <param name="reason">The reason to count.</param>
    /// <returns>The total count of declines for the reason.</returns>
    public long DeclinedFor(PlaneDeclineReason reason) => _declineCounts[(int)reason];

    /// <summary>
    /// Gets the nodes that the last plane selection declined. <see cref="DeclineReasons"/> holds the reason at the same index.
    /// It omits nodes that are not buffers and mirrored nodes.
    /// </summary>
    public IReadOnlyList<SceneNode> DeclinedCandidates => _declinedNodes;

    /// <summary>
    /// Gets the reason for each node in <see cref="DeclinedCandidates"/>, at the same index.
    /// </summary>
    public IReadOnlyList<PlaneDeclineReason> DeclineReasons => _declinedReasons;

    /// <summary>
    /// Gets the buffer nodes that the last commit put on overlay planes.
    /// </summary>
    public IReadOnlyList<SceneBuffer> OffloadedNodes => _offloadedPrev;

    /// <summary>
    /// Gets the plane box in framebuffer pixels for each node in <see cref="OffloadedNodes"/>, at the same index.
    /// </summary>
    public IReadOnlyList<Box> OffloadedBoxes => _offloadedPrevBoxes;

    /// <summary>
    /// Gets a short text that tells why the last composed commit tried no planes, for example "software cursor".
    /// It is <see langword="null"/> when the commit tried planes.
    /// </summary>
    public string? OffloadSkipped { get; private set; }

    /// <summary>
    /// Gets the post stages that run on each composed frame, in the order that they run.
    /// </summary>
    public IReadOnlyList<IPostStage> PostStages => _postStages;

    private void Decline(SceneNode node, PlaneDeclineReason reason)
    {
        _declineCounts[(int)reason]++;
        if (reason is not (PlaneDeclineReason.NotABuffer or PlaneDeclineReason.Mirrored))
        {
            _declinedNodes.Add(node);
            _declinedReasons.Add(reason);
        }
    }

    /// <summary>
    /// Occurs when a commit finds a new direct-scanout candidate, or finds none after it had one.
    /// The argument is the surface of the candidate, or <see langword="null"/>.
    /// </summary>
    public event Action<Surface?>? ScanoutCandidateChanged;

    /// <summary>
    /// Occurs when the set of overlay-plane candidates changes from the previous commit.
    /// The list is reused. A handler must copy it to keep it.
    /// </summary>
    public event Action<IReadOnlyList<SceneBuffer>>? OffloadCandidatesChanged;

    private readonly List<SceneBuffer> _announcedCandidates = [];

    /// <summary>
    /// Occurs when this output gets damage or a plane change that needs a commit. A handler usually schedules a frame.
    /// </summary>
    public event Action? DamagePending;

    private BufferLock _softwareCursor;
    private ITexture? _softwareCursorTexture;

    private bool _disposed;
    private int _cursorX, _cursorY, _cursorHotspotX, _cursorHotspotY;

    /// <summary>
    /// Sets the cursor image that composed frames draw on top. The output locks the buffer until the next call or disposal.
    /// A software cursor turns off direct scanout and plane offload.
    /// </summary>
    /// <param name="image">The cursor image, or <see langword="null"/> to remove the cursor.</param>
    /// <param name="hotspotX">The horizontal hotspot in image pixels.</param>
    /// <param name="hotspotY">The vertical hotspot in image pixels.</param>
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

    /// <summary>
    /// Moves the software cursor hotspot and damages the old and new cursor areas.
    /// </summary>
    /// <param name="x">The horizontal position in output-local physical pixels.</param>
    /// <param name="y">The vertical position in output-local physical pixels.</param>
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

    /// <summary>
    /// Detaches from the scene and the output and releases every held buffer, texture and region. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene.Detach(this);
        _scene.Damaged -= OnSceneDamaged;
        _scene.FrameRequested -= OnFrameRequested;
        _scene.ColorDescriptionChanged -= OnColorDescriptionChanged;
        _luts.Clear();
        _defaultLut = null;
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

    /// <summary>
    /// Gets a value indicating whether the next <see cref="Commit(IRenderer, Swapchain, OutputState, in SceneCommitOptions)"/> has work to do.
    /// It is <see langword="true"/> for damage, direct scanout, changed plane content or a requested plane commit.
    /// </summary>
    public bool NeedsRepaint => !Ring.IsEmpty || _scanningOut || _planeContentChanged || _planeCommitRequested;

    private bool _planeContentChanged;
    private bool _planeCommitRequested;

    /// <summary>
    /// Asks the next commit to send the planes again, even when nothing is damaged. It raises <see cref="DamagePending"/> once per request.
    /// </summary>
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

    /// <summary>
    /// Occurs at the start of each commit, before the scene is collected. A handler can move nodes for the frame that the tick describes.
    /// </summary>
    public event Action<FrameTick>? BeforeRepaint;

    private readonly List<IPostStage> _postStages = [];
    private MemoryBuffer? _postA;
    private MemoryBuffer? _postB;
    private ITexture? _postTextureA;
    private ITexture? _postTextureB;
    private IRenderer? _postTextureRenderer;

    /// <summary>
    /// Adds a stage that processes each composed frame after the stages already added. It damages the whole output.
    /// </summary>
    /// <param name="stage">The stage to add.</param>
    public void AddPostStage(IPostStage stage)
    {
        _postStages.Add(stage);
        Ring.AddWhole();
        DamagePending?.Invoke();
    }

    /// <summary>
    /// Removes a post stage and damages the whole output.
    /// </summary>
    /// <param name="stage">The stage to remove.</param>
    /// <returns><see langword="true"/> when the stage was present, otherwise <see langword="false"/>.</returns>
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

    /// <summary>
    /// Gets the filter that processes each composed frame, or <see langword="null"/> when there is none.
    /// </summary>
    public IFrameFilter? FrameFilter => _frameFilter;

    /// <summary>
    /// Sets the filter that processes each composed frame, and damages the whole output.
    /// The filter runs only when it and the renderer support filters.
    /// </summary>
    /// <param name="filter">The filter, or <see langword="null"/> to remove it.</param>
    /// <param name="allocator">The allocator for the scratch buffer, or <see langword="null"/> to use a memory buffer.</param>
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

    /// <summary>
    /// Draws the damaged part of the scene into the next swapchain buffer and commits it to the output.
    /// It can use direct scanout or overlay planes instead, when the options allow them.
    /// </summary>
    /// <param name="renderer">The renderer that draws the frame.</param>
    /// <param name="swapchain">The swapchain that supplies the target buffer and its age.</param>
    /// <param name="state">The state to fill and commit. The method clears it first.</param>
    /// <param name="options">The options for this frame.</param>
    /// <returns><see langword="true"/> when the output accepted a new frame. <see langword="false"/> when there was no damage, no buffer, or the output refused the commit.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="swapchain"/> is <see langword="null"/>.</exception>
    public bool Commit(IRenderer renderer, Swapchain swapchain, OutputState state, in SceneCommitOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(swapchain);
        return Commit(renderer, swapchain, null, 0, state, options);
    }

    /// <summary>
    /// Draws the damaged part of the scene into a buffer that the caller supplies and commits it to the output.
    /// </summary>
    /// <param name="renderer">The renderer that draws the frame.</param>
    /// <param name="target">The buffer to draw into.</param>
    /// <param name="age">The number of frames since the buffer was last drawn, or 0 when it is unknown.</param>
    /// <param name="state">The state to fill and commit. The method clears it first.</param>
    /// <param name="options">The options for this frame.</param>
    /// <returns><see langword="true"/> when the output accepted a new frame. <see langword="false"/> when there was no damage, or the output refused the commit.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="age"/> is negative.</exception>
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
            AllocationScope.Pause();
            try
            {
                BeforeRepaint?.Invoke(tick);
            }
            finally
            {
                AllocationScope.Resume();
            }

            var warm = _commitsEntered >= CommitScopeWarmup;
            if (!warm)
            {
                _commitsEntered++;
                return CommitLocked(renderer, swapchain, suppliedTarget, suppliedAge, state, options, mode, tick);
            }

            AllocationScope.Begin(region: "Commit", forgiving: true);
            try
            {
                return CommitLocked(renderer, swapchain, suppliedTarget, suppliedAge, state, options, mode, tick);
            }
            finally
            {
                AllocationScope.End();
            }
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

    /// <summary>
    /// Gets or sets the set that records the client surfaces that each composed frame shows. <see langword="null"/> records nothing.
    /// </summary>
    public PresentedSet? Presented { get; set; }

    private void RecordPresented(OutputMode mode)
    {
        if (Presented is not { } set)
        {
            return;
        }

        set.Begin();
        var output = new Box(0, 0, mode.Width, mode.Height);
        for (var i = 0; i < _renderList.Count; i++)
        {
            var entry = _renderList[i];
            if (entry.Node is not SceneBuffer { InputSurface: { IsDestroyed: false } surface } buffer)
            {
                continue;
            }

            var bounds = BufferBounds(buffer);
            if (bounds.IsEmpty || EntryPhysical(entry, bounds).Intersect(output).IsEmpty)
            {
                continue;
            }

            set.Add(surface);
        }
    }

    private bool _inCommit;
    private bool _damagedDuringCommit;
    private int _commitsEntered;

    private const int CommitScopeWarmup = 30;

    private bool CommitLocked(
        IRenderer renderer, Swapchain? swapchain, IBuffer? suppliedTarget, int suppliedAge,
        OutputState state, in SceneCommitOptions options,
        OutputMode mode, in FrameTick tick)
    {

        _scale = Output.Scale;
        _projection = ComputeProjection();
        _runsBackdropEffects = renderer.SupportsBackdropEffects;
        _blendsLinear = renderer.BlendSpace == RenderBlendSpace.Linear;
        Ring.Resize(mode.Width, mode.Height);
        _renderList.Clear();
        AllocationScope.Pause();
        try
        {
            _scene.CollectRenderList(_renderList, -_position.X, -_position.Y);
            _scene.PrepareCaptures(renderer, _renderList, _projection.Scale);
            RecordPresented(mode);
        }
        finally
        {
            AllocationScope.Resume();
        }

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
        _offloadedNowLayers.Clear();
        _layers.Clear();
        var sendLayers = false;
        OffloadSkipped = !options.AllowPlaneOffload ? "offload off"
            : _projection.IsTransformed ? "output transform"
            : _replicationSource is not null ? "replicated output"
            : _softwareCursor.Buffer is not null ? "software cursor"
            : _postStages.Count > 0 ? "post stage"
            : filter is not null ? "frame filter"
            : null;
        if (OffloadSkipped is null)
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

        var restoresHdr = _contentHdr is not null;
        if (restoresHdr)
        {
            state.SetHdr(OutputHdrMetadata);
        }

        var composedType = ContentTypeOverride ?? OutputContentType.None;
        var stagesType = composedType != _contentType;
        if (stagesType)
        {
            state.SetContentType(composedType);
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

        if (restoresHdr)
        {
            _contentHdr = null;
        }

        if (stagesType)
        {
            _contentType = composedType;
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

            var scaled = default(Box);
            var exact = entry.Transformed && entry.Transform.IsAxisAlignedScale && TryScaledPhysical(entry, bounds, out scaled);
            var physical = exact ? scaled : EntryPhysical(entry, bounds);
            if (physical.IsEmpty || physical.X >= mode.Width || physical.Y >= mode.Height || physical.Right <= 0 || physical.Bottom <= 0)
            {
                continue;
            }

            var onScreen = physical.Intersect(new Box(0, 0, mode.Width, mode.Height));
            var planeBox = onScreen;
            var declined = WhyNotAPlane(entry, physical, onScreen, maxLayers, exact, ref planeBox);
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
        in Scene.RenderEntry entry, in Box physical, in Box onScreen, int maxLayers, bool exactScale, ref Box planeBox)
    {
        if (entry.Mirrored)
        {
            return PlaneDeclineReason.Mirrored;
        }

        if (entry.Node is not SceneBuffer node)
        {
            return PlaneDeclineReason.NotABuffer;
        }

        if (LutFor(node) is not null)
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

        if (entry.Alpha < 1f || (entry.Transformed && !entry.Transform.IsAxisAlignedScale))
        {
            return PlaneDeclineReason.Transformed;
        }

        if (_blendsLinear && !node.IsOpaque)
        {
            return PlaneDeclineReason.BlendSpace;
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

        if (entry.Transformed && !exactScale)
        {
            return PlaneDeclineReason.ScaledFraction;
        }

        if (entry.Filter != TextureFilter.Linear
            && (node.SourceBox != default || physical.Width != content.Width || physical.Height != content.Height))
        {
            return PlaneDeclineReason.Filter;
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
        if (!HasSettled(node, planeBox))
        {
            return PlaneDeclineReason.Settling;
        }

        var layer = new PlaneRefusal(
            content.Width, content.Height, planeBox.Width, planeBox.Height, attributes.Format, attributes.Modifier, Output.PlaneEpoch);
        return WasRefused(node, layer) ? PlaneDeclineReason.BackendRefused : null;
    }

    private bool TryScaledPhysical(in Scene.RenderEntry entry, in Box bounds, out Box physical)
    {
        var logical = bounds.Translated(entry.X, entry.Y);
        var matrix = entry.Transform;
        var scale = _projection.Scale;
        var left = (((matrix.M11 * logical.X) + matrix.M13) * scale) - _projection.OriginX;
        var top = (((matrix.M22 * logical.Y) + matrix.M23) * scale) - _projection.OriginY;
        var right = (((matrix.M11 * logical.Right) + matrix.M13) * scale) - _projection.OriginX;
        var bottom = (((matrix.M22 * logical.Bottom) + matrix.M23) * scale) - _projection.OriginY;
        var x0 = Math.Round(left);
        var y0 = Math.Round(top);
        var x1 = Math.Round(right);
        var y1 = Math.Round(bottom);
        if (Math.Abs(left - x0) > ScaledSlack || Math.Abs(top - y0) > ScaledSlack ||
            Math.Abs(right - x1) > ScaledSlack || Math.Abs(bottom - y1) > ScaledSlack || x1 <= x0 || y1 <= y0)
        {
            physical = default;
            return false;
        }

        physical = new Box((int)x0, (int)y0, (int)(x1 - x0), (int)(y1 - y0));
        return true;
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
        AllocationScope.Pause();
        while (_layerPool.Count < _candidateNodes.Count)
        {
            _layerPool.Add(new OutputLayer());
        }

        AllocationScope.Resume();

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
            layer.YuvEncodingRefused = false;
            (layer.YuvCoefficients, layer.YuvRange) = node.InputSurface is { } surface && SurfaceHints is { } hints
                ? hints.YuvEncodingOf(surface)
                : (YuvCoefficients.Bt601, YuvRange.Limited);
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
                _offloadedNowLayers.Add(layer);
            }
            else
            {
                Decline(
                    _candidateNodes[i],
                    layer.Accepted ? PlaneDeclineReason.Demoted
                    : layer.YuvEncodingRefused ? PlaneDeclineReason.YuvEncoding
                    : PlaneDeclineReason.BackendRefused);
                if (!layer.Accepted)
                {
                    RememberRefusal(_candidateNodes[i], box);
                }

                _scratch.Reset(new PixmanBox32(box.X, box.Y, box.Right, box.Bottom));
                _compositedAbove.UnionWith(_scratch);
                if (layer.Accepted)
                {
                    layer.Buffer = null;
                }
            }
        }
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
            if (_offloadedNowLayers[i].Accepted)
            {
                continue;
            }

            Ring.Add(_offloadedNowBoxes[i]);
            _offloadedNow.RemoveAt(i);
            _offloadedNowBoxes.RemoveAt(i);
            _offloadedNowLayers.RemoveAt(i);
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

        AllocationScope.Pause();
        try
        {
            _announcedCandidates.Clear();
            _announcedCandidates.AddRange(_candidateNodes);
            OffloadCandidatesChanged?.Invoke(_announcedCandidates);
        }
        finally
        {
            AllocationScope.Resume();
        }
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

    /// <summary>
    /// Gets the buffer of the last composed frame that the output accepted. It is <see langword="null"/> before the first composed commit.
    /// </summary>
    public IBuffer? LastTarget { get; private set; }

    /// <summary>
    /// Gets the overlay layers that the last commit offered to the output. The list is reused by the next commit.
    /// </summary>
    public IReadOnlyList<OutputLayer> PresentedLayers => _layers;

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
            AllocationScope.Pause();
            try
            {
                _postStages[i].Render(pass, texture, new PostContext(mode.Width, mode.Height, tick, _postDamage));
            }
            finally
            {
                AllocationScope.Resume();
            }

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
        AllocationScope.Pause();
        var recorded = pass.AddFrameFilter(filter, texture, new FrameFilterOptions
        {
            FrameCount = _filterFrameCount,
            FramesPerSecond = tick.RefreshIntervalNanos > 0
                ? (float)(1_000_000_000.0 / tick.RefreshIntervalNanos)
                : 0f,
            FrametimeDeltaMillis = delta,
            Rotation = RotationOf(Output.Transform),
        });
        AllocationScope.Resume();
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
        if (_softwareCursor.Buffer is not { } cursorImage)
        {
            return;
        }

        if (_softwareCursorTexture is null)
        {
            AllocationScope.Pause();
            try
            {
                _softwareCursorTexture = renderer.ImportTexture(cursorImage);
            }
            finally
            {
                AllocationScope.Resume();
            }
        }

        if (_softwareCursorTexture is { } cursorTexture)
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

            if (!clip.IsEmpty && _runsBackdropEffects && SamplesBackdrop(entry.Node))
            {
                _scratch.Reset(new PixmanBox32(physical.X, physical.Y, physical.Right, physical.Bottom));
                _opaque.SubtractWith(_scratch);
                _scratch.IntersectWith(_damage);
                anyBackground.UnionWith(_scratch);
                continue;
            }

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

        var pass = renderer.BeginBufferPass(
            target, new RenderPassOptions { WaitFenceFd = waitFence, ColorDescription = _colorDescription });
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
                Scene.DrawEntry(renderer, pass, _renderList[i], clip, _projection, this);
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
            return entry.Node is SceneBuffer exact && exact.IsExactAt(_projection.Scale)
                ? _projection.MapPixels(exact.DeviceBox(logical.X, logical.Y, _projection.Scale))
                : _projection.Project(logical);
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

    private bool _blendsLinear;

    private bool IsOpaqueEntry(SceneNode node) => node switch
    {
        SceneRect rect => rect.IsOpaque,
        SceneBuffer buffer => buffer.IsOpaque && buffer.TextureShader is null && !(buffer.HasActiveBackdrop && _runsBackdropEffects),
        _ => false,
    };

    private static bool SamplesBackdrop(SceneNode node) => node switch
    {
        SceneBuffer buffer => buffer.HasActiveBackdrop,
        SceneTransform { Deformer: not null } deformed => SubtreeHasBackdrop(deformed),
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
        if (_regionPool.Count >= count)
        {
            return;
        }

        AllocationScope.Pause();
        while (_regionPool.Count < count)
        {
            _regionPool.Add(new PixmanRegion32());
        }

        AllocationScope.Resume();
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

        if (Output.Scale != _scale || (_replicationSource is null && Output.Transform != _projection.Transform))
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
                AllocationScope.Pause();
                try
                {
                    ScanoutCandidateChanged?.Invoke(null);
                }
                finally
                {
                    AllocationScope.Resume();
                }
            }

            _scanoutStreak = 0;
            return false;
        }

        var buffer = candidate.Buffer!;
        if (_scanoutStreak == 0)
        {
            AllocationScope.Pause();
            try
            {
                ScanoutCandidateChanged?.Invoke(candidate.InputSurface);
            }
            finally
            {
                AllocationScope.Resume();
            }
        }

        _scanoutStreak++;
        if (!_scanningOut && _scanoutStreak < ScanoutEntryThreshold)
        {
            return false;
        }

        var tearing = (state.Fields & OutputStateFields.Tearing) != 0 && state.Tearing;
        state.Clear();
        state.SetBuffer(buffer);
        if (tearing)
        {
            state.SetTearing(true);
        }

        if (candidate.AcquireFenceFd >= 0)
        {
            state.SetInFence(candidate.AcquireFenceFd);
        }

        var content = HdrMetadataPassthrough && OutputHdrMetadata is not null
            ? HdrStaticMetadata.FromContent(candidate.ColorDescription)
            : null;
        if (content != _contentHdr)
        {
            state.SetHdr(content ?? OutputHdrMetadata);
        }

        state.SetContentDescription(candidate.ColorDescription);
        var contentType = ContentTypeOverride ??
            (candidate.InputSurface is { } scanned && SurfaceHints is { } surfaceHints
                ? ContentTypeFor(surfaceHints.ContentTypeOf(scanned))
                : OutputContentType.None);
        if (contentType != _contentType)
        {
            state.SetContentType(contentType);
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

        _contentHdr = content;
        _contentType = contentType;
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
                entry.Node is SceneBuffer { TextureShader: null } buffer &&
                LutFor(buffer) is null &&
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
