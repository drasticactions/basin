using Basin.Diagnostics;
using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// Captures outputs, toplevels, layout regions and the cursor by drawing the scene into the target buffer.
/// It hides content that is excluded from capture while it draws.
/// </summary>
public sealed class SceneScreenCapture : IScreenCapture, IDisposable
{
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private readonly ExclusionWatch _watch;
    private CaptureCursorState _cursor;
    private IBuffer? _cursorBuffer;
    private IToplevelModel? _toplevels;
    private ToplevelInfo[] _exclusionScratch = new ToplevelInfo[16];
    private SceneNode?[] _hidden = new SceneNode?[16];

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneScreenCapture"/> class.
    /// </summary>
    /// <param name="scene">The scene to draw.</param>
    /// <param name="layout">The layout that places the outputs, for region and toplevel captures.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public SceneScreenCapture(Scene scene, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(layout);
        _scene = scene;
        _layout = layout;
        _watch = new ExclusionWatch(this);
    }

    /// <summary>
    /// Gets or sets the renderer that draws the captures. While it is <see langword="null"/>, no source is supported.
    /// </summary>
    public IRenderer? Renderer { get; set; }

    /// <summary>
    /// Gets or sets the color that fills the parts of a capture where no node draws.
    /// </summary>
    public RenderColor Background { get; set; }

    /// <summary>
    /// Gets or sets the toplevel model that resolves toplevel sources and exclusion states.
    /// A new model gets an observer that reports capture changes. <see langword="null"/> turns off toplevel captures.
    /// </summary>
    public IToplevelModel? Toplevels
    {
        get => _toplevels;
        set
        {
            if (ReferenceEquals(_toplevels, value))
            {
                return;
            }

            _toplevels?.RemoveObserver(_watch);
            _watch.Reset();
            _toplevels = value;
            _toplevels?.AddObserver(_watch);
        }
    }

    /// <summary>
    /// Gets or sets the map from toplevel ids to scene nodes. <see langword="null"/> turns off toplevel captures and the hiding of excluded toplevels.
    /// </summary>
    public ToplevelSceneIndex? Index { get; set; }

    private readonly CaptureDamageObservers _damageObservers = new();

    /// <inheritdoc/>
    public void AddDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Add(observer);

    /// <inheritdoc/>
    public void RemoveDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Remove(observer);

    /// <summary>
    /// Tells the damage observers that part of an output changed. The compositor calls it after each frame.
    /// </summary>
    /// <param name="output">The output that changed.</param>
    /// <param name="damage">The changed area in output buffer pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void NotifyDamaged(IOutput output, Box damage)
    {
        ArgumentNullException.ThrowIfNull(output);
        _damageObservers.Damaged(output, damage);
    }

    /// <inheritdoc/>
    public void SetCursor(IBuffer? image, in CaptureCursorState state)
    {
        var next = image is null || state.Width <= 0 || state.Height <= 0
            ? default
            : state with { IsVisible = true };
        if (ReferenceEquals(image, _cursorBuffer) && next == _cursor)
        {
            return;
        }

        _cursorBuffer = image;
        _cursor = next;
        _damageObservers.CursorChanged();
    }

    /// <inheritdoc/>
    public bool Supports(in CaptureSource source) => Renderer is null ? false : source.Kind switch
    {
        CaptureSourceKind.Output => true,
        CaptureSourceKind.Cursor => true,
        CaptureSourceKind.Toplevel => Toplevels is { } model &&
            (!model.TryGet(source.ToplevelId, out var info) ||
                (info.State & ToplevelState.ExcludedFromCapture) == 0),
        CaptureSourceKind.Region => IntersectsAnyOutput(source.LayoutBox),
        _ => false,
    };

    /// <inheritdoc/>
    public bool TryDescribe(in CaptureSource source, out CaptureFormat format)
    {
        format = default;
        switch (source.Kind)
        {
            case CaptureSourceKind.Output:
                if (source.OutputTarget is not { } output)
                {
                    return false;
                }

                var mode = output.CurrentMode;
                format = new CaptureFormat(mode.Width, mode.Height, DrmFormat.Xrgb8888);
                return true;

            case CaptureSourceKind.Cursor:
                if (!_cursor.IsVisible)
                {
                    return false;
                }

                format = new CaptureFormat(_cursor.Width, _cursor.Height, DrmFormat.Argb8888);
                return true;

            case CaptureSourceKind.Toplevel:
                if (!TryToplevelBox(source, out var box, out var scale))
                {
                    return false;
                }

                var physical = OutputScaling.ToPhysical(box, scale);
                format = new CaptureFormat(physical.Width, physical.Height, DrmFormat.Xrgb8888);
                return true;

            case CaptureSourceKind.Region:
                if (!IntersectsAnyOutput(source.LayoutBox))
                {
                    return false;
                }

                var regionScale = ResolveRegionScale(source);
                var regionPhysical = OutputScaling.ToPhysical(source.LayoutBox, regionScale);
                format = new CaptureFormat(regionPhysical.Width, regionPhysical.Height, DrmFormat.Xrgb8888);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Gets or sets the allocator for the intermediate buffer of a YUV capture. <see langword="null"/> uses a memory buffer.
    /// </summary>
    public IAllocator? Allocator { get; set; }

    /// <summary>
    /// Gets or sets the matrix that a YUV capture uses. The default is BT.709.
    /// </summary>
    public YuvCoefficients YuvCoefficients { get; set; } = YuvCoefficients.Bt709;

    /// <summary>
    /// Gets or sets the value range that a YUV capture uses. The default is the limited range.
    /// </summary>
    public YuvRange YuvRange { get; set; } = YuvRange.Limited;

    /// <inheritdoc/>
    public bool CanCaptureYuv => Renderer is { CanConvertYuv: true };

    /// <inheritdoc/>
    public bool CanCaptureHalfFloat => Renderer is { RendersHalfFloat: true };

    /// <summary>
    /// Gets the type name of <see cref="Renderer"/>, or <see langword="null"/> when no renderer is set.
    /// </summary>
    public string? RendererName => Renderer?.GetType().Name;

    /// <summary>
    /// Gets the color space of a capture. An output or region capture uses the color space of its scene output.
    /// A toplevel capture is always SDR.
    /// </summary>
    /// <param name="source">The source to capture.</param>
    /// <param name="color">The color description and HDR metadata, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> for output, region and toplevel sources, otherwise <see langword="false"/>.</returns>
    public bool TryColorOf(in CaptureSource source, out CaptureColor color)
    {
        color = default;
        SceneOutput? table;
        switch (source.Kind)
        {
            case CaptureSourceKind.Output when source.OutputTarget is { } output:
                table = TableFor(output);
                break;
            case CaptureSourceKind.Region:
                table = TableFor(null);
                break;
            case CaptureSourceKind.Toplevel:
                color = new CaptureColor(ImageDescription.SdrDefault, null);
                return true;
            default:
                return false;
        }

        color = new CaptureColor(
            table?.ColorDescription ?? ImageDescription.SdrDefault,
            table?.ContentHdrMetadata ?? table?.OutputHdrMetadata);
        return true;
    }

    private static bool IsHighDynamicRange(ImageDescription? description) =>
        description?.TransferNamed is ColorTransferFunction.St2084Pq or ColorTransferFunction.Hlg;

    private IBuffer? _intermediate;
    private ITexture? _intermediateTexture;

    /// <inheritdoc/>
    public bool Capture(in CaptureSource source, in Box region, IBuffer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Format == DrmFormat.Nv12)
        {
            return CaptureYuv(source, region, target);
        }

        return source.Kind switch
        {
            CaptureSourceKind.Output => CaptureOutput(source.OutputTarget!, region, target, source.OverlayCursor),
            CaptureSourceKind.Toplevel => CaptureToplevel(source, region, target),
            CaptureSourceKind.Cursor => CaptureCursor(target),
            CaptureSourceKind.Region => CaptureRegion(source, region, target),
            _ => false,
        };
    }

    private bool CaptureYuv(in CaptureSource source, in Box region, IBuffer target)
    {
        if (Renderer is not { CanConvertYuv: true } renderer || source.Kind == CaptureSourceKind.Cursor ||
            !TryDescribe(source, out var format))
        {
            return false;
        }

        var width = region.IsEmpty ? format.Width : region.Width;
        var height = region.IsEmpty ? format.Height : region.Height;
        if (_intermediate is not { } intermediate || intermediate.Width != width || intermediate.Height != height)
        {
            AllocationScope.Pause();
            try
            {
                DropIntermediate();
                intermediate = AllocateIntermediate(renderer, width, height);
                _intermediate = intermediate;
                if (!Capture(source, region, intermediate))
                {
                    return false;
                }
            }
            finally
            {
                AllocationScope.Resume();
            }
        }
        else if (!Capture(source, region, intermediate))
        {
            return false;
        }

        if (_intermediateTexture is null)
        {
            AllocationScope.Pause();
            try
            {
                _intermediateTexture = renderer.ImportTexture(intermediate);
            }
            finally
            {
                AllocationScope.Resume();
            }
        }

        if (_intermediateTexture is not { } texture)
        {
            return false;
        }

        if (texture is IRefreshableTexture refreshable)
        {
            refreshable.MarkDirty();
        }

        return renderer.ConvertToYuv(texture, default, target, YuvCoefficients, YuvRange);
    }

    private IBuffer AllocateIntermediate(IRenderer renderer, int width, int height)
    {
        if (Allocator is { } allocator)
        {
            var modifiers = allocator.Formats.ModifiersOf(DrmFormat.Xrgb8888)
                .Where(modifier => renderer.DmabufTextureFormats.Contains(DrmFormat.Xrgb8888, modifier))
                .ToArray();
            if (modifiers.Length > 0 &&
                allocator.Allocate(width, height, DrmFormat.Xrgb8888, modifiers, BufferUse.Render) is { } buffer)
            {
                return buffer;
            }
        }

        return new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
    }

    /// <summary>
    /// Releases the intermediate buffer and texture of YUV captures. The object stays usable.
    /// </summary>
    public void Dispose() => DropIntermediate();

    private void DropIntermediate()
    {
        _intermediateTexture?.Dispose();
        _intermediateTexture = null;
        (_intermediate as BufferBase)?.Destroy();
        _intermediate = null;
    }

    /// <inheritdoc/>
    public bool TryCursorState(IOutput output, out CaptureCursorState cursor)
    {
        ArgumentNullException.ThrowIfNull(output);
        var projection = OutputProjection.For(output);
        if (!TryCursorPixels(output, projection, out var x, out var y))
        {
            cursor = default;
            return false;
        }

        var (mappedX, mappedY) = projection.MapPoint(x, y);
        cursor = _cursor with { X = mappedX, Y = mappedY };
        return true;
    }

    private bool TryCursorPixels(IOutput output, in OutputProjection projection, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!_cursor.IsVisible)
        {
            return false;
        }

        var box = _layout.BoxOf(output);
        var scale = output.Scale;
        x = (int)Math.Round((_cursor.X - box.X) * scale);
        y = (int)Math.Round((_cursor.Y - box.Y) * scale);
        var left = x - _cursor.HotspotX;
        var top = y - _cursor.HotspotY;
        return left + _cursor.Width > 0 && top + _cursor.Height > 0 &&
            left < projection.Width && top < projection.Height;
    }

    private bool CaptureOutput(IOutput output, in Box region, IBuffer target, bool overlayCursor)
    {
        var box = _layout.BoxOf(output);
        var projection = OutputProjection.For(output).CroppedTo(region.X, region.Y);
        var hidden = HideExcluded();
        try
        {
            if (!RenderAt(box.X, box.Y, projection, target, output))
            {
                return false;
            }
        }
        finally
        {
            RestoreHidden(hidden);
        }

        return !overlayCursor || DrawCursorOver(output, projection, target);
    }

    private int HideExcluded()
    {
        var hidden = 0;
        HideFlagged(_scene.Root, ref hidden);
        if (Toplevels is not { } model || Index is not { } index)
        {
            return hidden;
        }

        var count = model.Enumerate(_exclusionScratch);
        while (count < 0)
        {
            _exclusionScratch = new ToplevelInfo[_exclusionScratch.Length * 2];
            count = model.Enumerate(_exclusionScratch);
        }

        for (var i = 0; i < count; i++)
        {
            if ((_exclusionScratch[i].State & ToplevelState.ExcludedFromCapture) == 0 ||
                !index.TryGet(_exclusionScratch[i].Id, out var trees))
            {
                continue;
            }

            if (trees.Content is { Enabled: true } content)
            {
                Hide(content, ref hidden);
            }

            if (trees.Popups is { Enabled: true } popups)
            {
                Hide(popups, ref hidden);
            }
        }

        return hidden;
    }

    private void HideFlagged(SceneNode node, ref int hidden)
    {
        if (!node.Enabled)
        {
            return;
        }

        if (node.ExcludedFromCapture)
        {
            Hide(node, ref hidden);
            return;
        }

        if (node is SceneTree tree)
        {
            var children = tree.Children;
            for (var i = 0; i < children.Count; i++)
            {
                HideFlagged(children[i], ref hidden);
            }
        }
    }

    private void Hide(SceneNode node, ref int hidden)
    {
        if (hidden == _hidden.Length)
        {
            Array.Resize(ref _hidden, _hidden.Length * 2);
        }

        node.SetEnabledForCapture(false);
        _hidden[hidden++] = node;
    }

    private void RestoreHidden(int hidden)
    {
        for (var i = 0; i < hidden; i++)
        {
            _hidden[i]!.SetEnabledForCapture(true);
            _hidden[i] = null;
        }
    }

    private bool DrawCursorOver(IOutput output, in OutputProjection projection, IBuffer target)
    {
        if (_cursorBuffer is not { } image ||
            Renderer is not { } renderer ||
            !TryCursorPixels(output, projection, out var x, out var y))
        {
            return true;
        }

        using var texture = renderer.ImportTexture(image);
        if (texture is null)
        {
            return true;
        }

        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddTexture(texture, new TextureRenderOptions
        {
            SrcBox = new FBox(0, 0, _cursor.Width, _cursor.Height),
            DstBox = new Box(x - _cursor.HotspotX, y - _cursor.HotspotY, _cursor.Width, _cursor.Height),
            Transform = projection.MapsPixels ? projection.Matrix : RenderTransform.Identity,
        });
        return pass.Submit();
    }

    private bool CaptureToplevel(in CaptureSource source, in Box region, IBuffer target)
    {
        if (!TryToplevelBox(source, out var box, out var scale))
        {
            return false;
        }

        if (Renderer is { } renderer && Index is { } index &&
            index.TryGet(source.ToplevelId, out var trees) && trees.Content is { } content)
        {
            var client = source.ClientOnly ? trees.Client ?? content : content;
            Box? popupClip = source.ClientOnly ? box : null;
            var hiddenNodes = HideExcluded();
            bool rendered;
            try
            {
                rendered = _scene.RenderSubtrees(
                    renderer, client, trees.Popups, popupClip, target, box.X + region.X, box.Y + region.Y, scale,
                    Background, TableFor(null));
            }
            finally
            {
                RestoreHidden(hiddenNodes);
            }

            return rendered && (!source.OverlayCursor || DrawCursorOverRegion(box, region, scale, target));
        }

        var hidden = HideExcluded();
        try
        {
            return RenderAt(box.X + region.X, box.Y + region.Y, scale, target);
        }
        finally
        {
            RestoreHidden(hidden);
        }
    }

    private static bool TryClientBox(SceneNode client, out Box box)
    {
        var bounds = client.SubtreeBounds();
        if (bounds.IsEmpty)
        {
            box = default;
            return false;
        }

        var (x, y) = client.ScenePosition;
        box = new Box(x + bounds.X, y + bounds.Y, bounds.Width, bounds.Height);
        return true;
    }

    private bool CaptureCursor(IBuffer target)
    {
        if (_cursorBuffer is not { } cursor || !_cursor.IsVisible)
        {
            return false;
        }

        if (Renderer is not { } renderer)
        {
            return false;
        }

        using var texture = renderer.ImportTexture(cursor);
        if (texture is null)
        {
            return false;
        }

        var width = Math.Min(target.Width, _cursor.Width);
        var height = Math.Min(target.Height, _cursor.Height);
        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddTexture(texture, new TextureRenderOptions
        {
            SrcBox = new FBox(0, 0, width, height),
            DstBox = new Box(0, 0, width, height),
        });
        return pass.Submit();
    }

    private bool IntersectsAnyOutput(in Box box)
    {
        foreach (var (output, _) in _layout.Outputs)
        {
            if (!_layout.BoxOf(output).Intersect(box).IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private double ResolveRegionScale(in CaptureSource source)
    {
        if (source.Scale > 0)
        {
            return source.Scale;
        }

        double scale = 1;
        foreach (var (output, _) in _layout.Outputs)
        {
            if (!_layout.BoxOf(output).Intersect(source.LayoutBox).IsEmpty)
            {
                scale = Math.Max(scale, output.Scale);
            }
        }

        return scale;
    }

    private bool CaptureRegion(in CaptureSource source, in Box crop, IBuffer target)
    {
        if (!IntersectsAnyOutput(source.LayoutBox))
        {
            return false;
        }

        var scale = ResolveRegionScale(source);
        var box = source.LayoutBox;
        var hidden = HideExcluded();
        try
        {
            if (!RenderAt(box.X + crop.X, box.Y + crop.Y, scale, target))
            {
                return false;
            }
        }
        finally
        {
            RestoreHidden(hidden);
        }

        return !source.OverlayCursor || DrawCursorOverRegion(box, crop, scale, target);
    }

    private bool DrawCursorOverRegion(in Box box, in Box crop, double scale, IBuffer target)
    {
        if (_cursorBuffer is not { } image || Renderer is not { } renderer || !_cursor.IsVisible)
        {
            return true;
        }

        var x = (int)Math.Round((_cursor.X - box.X - crop.X) * scale);
        var y = (int)Math.Round((_cursor.Y - box.Y - crop.Y) * scale);
        var left = x - _cursor.HotspotX;
        var top = y - _cursor.HotspotY;
        if (left + _cursor.Width <= 0 || top + _cursor.Height <= 0 ||
            left >= target.Width || top >= target.Height)
        {
            return true;
        }

        using var texture = renderer.ImportTexture(image);
        if (texture is null)
        {
            return true;
        }

        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddTexture(texture, new TextureRenderOptions
        {
            SrcBox = new FBox(0, 0, _cursor.Width, _cursor.Height),
            DstBox = new Box(left, top, _cursor.Width, _cursor.Height),
        });
        return pass.Submit();
    }

    private bool RenderAt(int originX, int originY, double scale, IBuffer target, IOutput? output = null) =>
        RenderAt(originX, originY, new OutputProjection(scale), target, output);

    private bool RenderAt(int originX, int originY, in OutputProjection projection, IBuffer target, IOutput? output = null)
    {
        if (Renderer is not { } renderer)
        {
            return false;
        }

        var table = TableFor(output);
        var sdr = target.Format is DrmFormat.Argb8888 or DrmFormat.Xrgb8888 or DrmFormat.Abgr8888 or DrmFormat.Xbgr8888 or DrmFormat.Rgb565 &&
            IsHighDynamicRange(table?.ColorDescription);
        return _scene.Render(renderer, target, new SceneRenderOptions
        {
            Background = Background,
            Projection = projection,
            OriginX = originX,
            OriginY = originY,
            Luts = sdr ? null : table,
            ColorDescription = sdr ? ImageDescription.SdrDefault : table?.ColorDescription,
        });
    }

    private SceneOutput? TableFor(IOutput? output)
    {
        var outputs = _scene.Outputs;
        for (var i = 0; i < outputs.Count; i++)
        {
            if (output is null || outputs[i].Output == output)
            {
                return outputs[i];
            }
        }

        return null;
    }

    private bool TryToplevelBox(in CaptureSource source, out Box box, out double scale)
    {
        box = default;
        scale = 1;
        if (Toplevels is not { } model || !model.TryGet(source.ToplevelId, out var info) ||
            (info.State & ToplevelState.ExcludedFromCapture) != 0)
        {
            return false;
        }

        if (source.ClientOnly && Index is { } index && index.TryGet(source.ToplevelId, out var trees) &&
            trees.Client is { } client && TryClientBox(client, out box))
        {
        }
        else if (info.Geometry.IsEmpty)
        {
            return false;
        }
        else
        {
            box = info.Geometry;
        }

        foreach (var (output, _) in _layout.Outputs)
        {
            if (!_layout.BoxOf(output).Intersect(box).IsEmpty)
            {
                scale = Math.Max(scale, output.Scale);
            }
        }

        return true;
    }

    private void OnExclusionFlipped(in ToplevelInfo info)
    {
        foreach (var (output, _) in _layout.Outputs)
        {
            var box = _layout.BoxOf(output);
            if (info.Geometry.IsEmpty || !box.Intersect(info.Geometry).IsEmpty)
            {
                _damageObservers.Damaged(output, new Box(0, 0, box.Width, box.Height));
            }
        }
    }

    private sealed class ExclusionWatch(SceneScreenCapture owner) : IToplevelObserver
    {
        private readonly Dictionary<ulong, bool> _excluded = [];

        public void Reset() => _excluded.Clear();

        public void OnToplevelAdded(ulong toplevelId) => Track(toplevelId, damageOnFlip: false);

        public void OnToplevelChanged(ulong toplevelId) => Track(toplevelId, damageOnFlip: true);

        public void OnToplevelRemoved(ulong toplevelId) => _excluded.Remove(toplevelId);

        private void Track(ulong toplevelId, bool damageOnFlip)
        {
            if (owner.Toplevels is not { } model || !model.TryGet(toplevelId, out var info))
            {
                return;
            }

            var excluded = (info.State & ToplevelState.ExcludedFromCapture) != 0;
            if (_excluded.TryGetValue(toplevelId, out var was) && was == excluded)
            {
                return;
            }

            _excluded[toplevelId] = excluded;
            if (damageOnFlip)
            {
                owner.OnExclusionFlipped(info);
            }
        }
    }
}
