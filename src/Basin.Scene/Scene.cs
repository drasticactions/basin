using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// Frame callbacks, surface collection and full rendering for the scene.
/// </summary>
public sealed partial class Scene
{
    private readonly List<RenderEntry> _renderList = [];

    private readonly Dictionary<IBuffer, TextureCacheEntry> _textures = [];

    private sealed class TextureCacheEntry
    {
        private readonly Scene _scene;

        public TextureCacheEntry(Scene scene, IRenderer renderer, ITexture texture)
        {
            _scene = scene;
            Renderer = renderer;
            Texture = texture;
        }

        public Action DropOnRelease => _dropOnRelease ??= () =>
        {
            if (Watched is { } watched)
            {
                _scene.DropTexture(watched, this);
            }
        };

        private Action? _dropOnRelease;

        public IRenderer Renderer { get; }

        public ITexture Texture { get; }

        public IBuffer? Watched { get; set; }

        public bool DropsOnRelease { get; set; }
    }

    private readonly List<IBuffer> _deadTextures = [];
    private Action? _dropDestroyed;

    private void WatchBufferEnd(IBuffer buffer, TextureCacheEntry entry)
    {
        entry.Watched = buffer;
        entry.DropsOnRelease = buffer.IsDestroyed;
        if (entry.DropsOnRelease)
        {
            buffer.Released += entry.DropOnRelease;
        }
        else
        {
            _dropDestroyed ??= DropDestroyedTextures;
            buffer.Destroyed += _dropDestroyed;
        }
    }

    private void UnwatchBufferEnd(IBuffer buffer, TextureCacheEntry entry)
    {
        if (entry.Watched is null)
        {
            return;
        }

        if (entry.DropsOnRelease)
        {
            buffer.Released -= entry.DropOnRelease;
        }
        else if (_dropDestroyed is { } handler)
        {
            buffer.Destroyed -= handler;
        }

        entry.Watched = null;
    }

    private void DropDestroyedTextures()
    {
        foreach (var pair in _textures)
        {
            if (!pair.Value.DropsOnRelease && pair.Key.IsDestroyed)
            {
                _deadTextures.Add(pair.Key);
            }
        }

        foreach (var buffer in _deadTextures)
        {
            if (_textures.TryGetValue(buffer, out var entry))
            {
                DropTexture(buffer, entry);
            }
        }

        _deadTextures.Clear();
    }

    internal ITexture? TextureFor(IRenderer renderer, IBuffer buffer)
    {
        if (_textures.TryGetValue(buffer, out var entry))
        {
            if (ReferenceEquals(entry.Renderer, renderer))
            {
                return entry.Texture;
            }

            DropTexture(buffer, entry);
        }

        AllocationScope.Pause();
        try
        {
            var texture = renderer.ImportTexture(buffer);
            if (texture is null)
            {
                return null;
            }

            var created = new TextureCacheEntry(this, renderer, texture);
            _textures[buffer] = created;
            WatchBufferEnd(buffer, created);
            return texture;
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    internal bool TryAdoptTexture(IRenderer renderer, IBuffer from, IBuffer to, in DamageRects damage, bool full)
    {
        if (ReferenceEquals(from, to) ||
            _textures.TryGetValue(to, out _) ||
            !_textures.TryGetValue(from, out var entry) ||
            !ReferenceEquals(entry.Renderer, renderer) ||
            entry.Texture is not IRefreshableTexture refreshable ||
            from.LockCount > 1)
        {
            return false;
        }

        var whole = full || damage.Count == 0;
        if (!refreshable.TryAdopt(to, whole ? new Box(0, 0, to.Width, to.Height) : damage[0]))
        {
            return false;
        }

        if (!whole)
        {
            for (var i = 1; i < damage.Count; i++)
            {
                refreshable.MarkDirty(damage[i]);
            }
        }

        UnwatchBufferEnd(from, entry);
        _textures.Remove(from);
        _textures[to] = entry;
        WatchBufferEnd(to, entry);
        return true;
    }

    internal ITexture? PeekTexture(IBuffer buffer) =>
        _textures.TryGetValue(buffer, out var entry) ? entry.Texture : null;

    private void DropTexture(IBuffer buffer, TextureCacheEntry entry)
    {
        UnwatchBufferEnd(buffer, entry);
        _textures.Remove(buffer);
        entry.Texture.Dispose();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Scene"/> class with an empty <see cref="Root"/> tree.
    /// </summary>
    public Scene()
    {
        Root = new SceneTree(null) { Owner = null! };
        Root.Owner = this;
    }

    private readonly List<Frame> _frames = [];

    /// <summary>
    /// Gets the root tree. Only nodes under this root belong to the scene and draw.
    /// </summary>
    public SceneTree Root { get; }

    /// <summary>
    /// Gets or sets the conversion that a buffer node tries when the renderer cannot import a buffer directly, for example a dmabuf from another GPU.
    /// </summary>
    public Func<IBuffer, ICrossDeviceConversion?>? CrossDeviceImport { get; set; }

    /// <summary>
    /// Occurs when a visible part of the scene changes. The box is in scene coordinates, in logical pixels. The node is the source of the damage, if known.
    /// </summary>
    public event Action<SceneNode?, Box>? Damaged;

    /// <summary>
    /// Occurs when a visible node moves, resizes, restacks, changes its clip, or is enabled, disabled or destroyed.
    /// </summary>
    public event Action? StructureChanged;

    /// <summary>
    /// Occurs when a UI surface node changes the region where it accepts input.
    /// </summary>
    public event Action? InputRegionChanged;

    /// <summary>
    /// Occurs when a visible or held surface commits frame callbacks or presentation feedback, or when <see cref="RequestFrame"/> runs.
    /// </summary>
    public event Action? FrameRequested;

    /// <summary>
    /// Gets the frames that have attached to this scene, in the order they attached.
    /// </summary>
    public IReadOnlyList<Frame> Frames => _frames;

    /// <summary>
    /// Occurs after a frame in this scene draws its chrome.
    /// </summary>
    public event Action<Frame>? FrameDrawn;

    internal void AddFrame(Frame frame)
    {
        if (!_frames.Contains(frame))
        {
            _frames.Add(frame);
        }
    }

    internal void RemoveFrame(Frame frame) => _frames.Remove(frame);

    internal void NotifyFrameDrawn(Frame frame) => FrameDrawn?.Invoke(frame);

    internal bool HasDamageHandlers => Damaged is not null;

    internal bool HasFrameDrawnHandlers => FrameDrawn is not null;

    internal void NotifyStructureChanged() => StructureChanged?.Invoke();

    internal void NotifyInputRegionChanged() => InputRegionChanged?.Invoke();

    internal void NotifyFrameRequested() => FrameRequested?.Invoke();

    /// <summary>
    /// Raises <see cref="FrameRequested"/> so that the outputs schedule a repaint.
    /// </summary>
    public void RequestFrame() => NotifyFrameRequested();

    /// <summary>
    /// Sends the frame-done events of every surface on an enabled path in the scene, and of the surfaces that a holder forwards. It does nothing when no surface waits.
    /// </summary>
    /// <param name="timestampMs">The frame time, in milliseconds.</param>
    public void SendFrameDone(uint timestampMs)
    {
        var held = _held.Count > 0 && HeldHaveFrameWork();
        if (!held && !HasFrameWork(Root))
        {
            return;
        }

        AllocationScope.Begin(warmup: 1);
        SendFrameDone(Root, timestampMs);
        if (held)
        {
            SendHeldFrameDone(timestampMs);
        }

        AllocationScope.End();
    }

    private static bool HasFrameWork(SceneNode node)
    {
        if (!node.Enabled)
        {
            return false;
        }

        switch (node)
        {
            case SceneBuffer { InputSurface: { IsDestroyed: false } surface }:
                return surface.Current.FrameResources.Count > 0 || surface.Current.FrameCallbacks.Count > 0;
            case SceneTree tree:
                foreach (var child in tree.Children)
                {
                    if (HasFrameWork(child))
                    {
                        return true;
                    }
                }

                break;
        }

        return false;
    }

    private readonly Dictionary<SceneTree, SceneTree> _held = [];

    internal void Hold(SceneTree live, SceneTree holder) => _held[live] = holder;

    internal void Release(SceneTree live, SceneTree holder)
    {
        if (_held.TryGetValue(live, out var current) && ReferenceEquals(current, holder))
        {
            _held.Remove(live);
        }
    }

    internal SceneTree? HolderOf(SceneTree live) => _held.TryGetValue(live, out var holder) ? holder : null;

    private bool Forwards(SceneTree live, SceneTree holder) =>
        !live.Enabled && !live.IsDestroyed && !holder.IsDestroyed && ReferenceEquals(holder.OwnerIfVisible(out _, out _), this);

    private bool HeldHaveFrameWork()
    {
        foreach (var (live, holder) in _held)
        {
            if (!Forwards(live, holder))
            {
                continue;
            }

            foreach (var child in live.Children)
            {
                if (HasFrameWork(child))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void SendHeldFrameDone(uint timestampMs)
    {
        foreach (var (live, holder) in _held)
        {
            if (!Forwards(live, holder))
            {
                continue;
            }

            foreach (var child in live.Children)
            {
                SendFrameDone(child, timestampMs);
            }
        }
    }

    private static void SendFrameDone(SceneNode node, uint timestampMs)
    {
        if (!node.Enabled)
        {
            return;
        }

        switch (node)
        {
            case SceneBuffer { InputSurface: { IsDestroyed: false } surface }:
                surface.SendFrameDone(timestampMs);
                break;
            case SceneTree tree:
                foreach (var child in tree.Children)
                {
                    SendFrameDone(child, timestampMs);
                }

                break;
        }
    }

    /// <summary>
    /// Lists every live surface on an enabled path with its box in scene coordinates. A transformed surface gets the bounding box of its transformed area.
    /// </summary>
    /// <param name="into">The list to fill. The method clears it first.</param>
    public void CollectSurfaces(List<SurfaceBox> into)
    {
        into.Clear();
        CollectSurfaces(Root, Root.X, Root.Y, into);
    }

    private static void CollectSurfaces(SceneNode node, int x, int y, List<SurfaceBox> into)
    {
        if (!node.Enabled)
        {
            return;
        }

        switch (node)
        {
            case SceneBuffer { InputSurface: { IsDestroyed: false } surface } buffer:
                var (width, height) = buffer.Size;
                into.Add(new SurfaceBox(surface, new Box(x, y, width, height)));
                break;
            case SceneTransform transform:
                var start = into.Count;
                foreach (var child in transform.Children)
                {
                    CollectSurfaces(child, x + child.X, y + child.Y, into);
                }

                if (transform.Deformer is null && transform.Matrix.IsIdentity)
                {
                    break;
                }

                for (var i = start; i < into.Count; i++)
                {
                    var box = into[i].Box.Translated(-x, -y);
                    if (transform.Deformer is { } deformer)
                    {
                        box = deformer.MapBounds(box);
                    }

                    if (!transform.Matrix.IsIdentity && transform.Matrix.TryMapBounds(box, out var hull))
                    {
                        box = hull;
                    }

                    into[i] = new SurfaceBox(into[i].Surface, box.Translated(x, y));
                }

                break;
            case SceneTree tree:
                foreach (var child in tree.Children)
                {
                    CollectSurfaces(child, x + child.X, y + child.Y, into);
                }

                break;
        }
    }

    internal void NotifyDamage(SceneNode? source, in Box box)
    {
        Damaged?.Invoke(source, box);
        ForwardToMirrors(source);
    }

    internal void NotifyDamage(SceneNode? source, PixmanRegion32 region, int sceneX, int sceneY, in Box bounds)
    {
        foreach (var rect in RegionRects.Of(region))
        {
            var x1 = Math.Max(rect.X1, bounds.X);
            var y1 = Math.Max(rect.Y1, bounds.Y);
            var x2 = Math.Min(rect.X2, bounds.Right);
            var y2 = Math.Min(rect.Y2, bounds.Bottom);
            if (x2 > x1 && y2 > y1)
            {
                Damaged?.Invoke(source, new Box(sceneX + x1, sceneY + y1, x2 - x1, y2 - y1));
            }
        }

        ForwardToMirrors(source);
    }

    private const int MaxMirrorDepth = 3;

    private int _mirrorCount;
    private int _mirrorDepth;

    internal void AddMirror() => _mirrorCount++;

    internal void RemoveMirror() => _mirrorCount--;

    private void ForwardToMirrors(SceneNode? source)
    {
        if (_mirrorCount == 0 || source is null || _mirrorDepth >= MaxMirrorDepth)
        {
            return;
        }

        _mirrorDepth++;
        for (SceneNode? node = source; node is not null; node = node.Parent)
        {
            if (node is SceneTree { Mirrors: { } mirrors })
            {
                for (var i = mirrors.Count - 1; i >= 0; i--)
                {
                    mirrors[i].DamageSubtree();
                }
            }
        }

        _mirrorDepth--;
    }

    /// <summary>
    /// Repaints the whole scene into a buffer, with no damage tracking.
    /// </summary>
    /// <param name="renderer">The renderer that draws.</param>
    /// <param name="target">The buffer to draw into.</param>
    /// <param name="background">The color that fills the buffer before the scene draws.</param>
    /// <param name="scale">The ratio of buffer pixels to logical pixels.</param>
    /// <returns>The result of <see cref="IRenderPass.Submit"/>.</returns>
    public bool Render(IRenderer renderer, IBuffer target, RenderColor background, double scale = 1.0) =>
        Render(renderer, target, new SceneRenderOptions { Background = background, Scale = scale });

    /// <summary>
    /// Repaints the whole scene into a buffer, with no damage tracking. This is the reference result that <see cref="SceneOutput"/> commits are tested against.
    /// </summary>
    /// <param name="renderer">The renderer that draws.</param>
    /// <param name="target">The buffer to draw into.</param>
    /// <param name="options">The background, projection, origin and color settings.</param>
    /// <returns>The result of <see cref="IRenderPass.Submit"/>.</returns>
    public bool Render(IRenderer renderer, IBuffer target, in SceneRenderOptions options)
    {
        var projection = options.Projection;
        _renderList.Clear();
        CollectTree(Root, Root.X - options.OriginX, Root.Y - options.OriginY, Unclipped, _renderList);
        PrepareCaptures(renderer, _renderList, projection.Scale);

        var targetBox = new Box(0, 0, target.Width, target.Height);

        var waitFence = GatherAcquireFences(_renderList, out var ownsFence);

        var pass = renderer.BeginBufferPass(
            target,
            new RenderPassOptions { WaitFenceFd = waitFence, ColorDescription = options.ColorDescription, SolidColorLut = options.Luts?.SolidColorLut });
        if (ownsFence)
        {
            RenderFences.CloseFence(waitFence);
        }

        pass.AddRect(options.Background, targetBox);

        for (var i = 0; i < _renderList.Count; i++)
        {
            DrawEntry(renderer, pass, _renderList[i], clip: null, projection, options.Luts);
        }

        return pass.Submit();
    }

    internal bool RenderSubtrees(
        IRenderer renderer, SceneNode? content, SceneNode? popups, Box? popupClip, IBuffer target,
        int originX, int originY, double scale, RenderColor background, IColorLutTable? luts)
    {
        var list = RentList();
        try
        {
            if (content is SceneTree contentTree)
            {
                var (contentX, contentY) = contentTree.OffsetFromRoot;
                CollectTree(
                    contentTree, contentX - originX, contentY - originY, Unclipped, RenderTransform.Identity,
                    transformed: false, contentTree.Alpha, list, mirrorDepth: 0, InheritedFilter(contentTree));
            }

            if (popups is SceneTree popupTree)
            {
                var clip = popupClip is { } box ? box.Translated(-originX, -originY) : Unclipped;
                if (!clip.IsEmpty)
                {
                    var (popupX, popupY) = popupTree.OffsetFromRoot;
                    CollectTree(
                        popupTree, popupX - originX, popupY - originY, clip, RenderTransform.Identity,
                        transformed: false, popupTree.Alpha, list, mirrorDepth: 0, InheritedFilter(popupTree));
                }
            }

            PrepareCaptures(renderer, list, scale);
            var waitFence = GatherAcquireFences(list, out var ownsFence);
            var pass = renderer.BeginBufferPass(target, new RenderPassOptions { WaitFenceFd = waitFence });
            if (ownsFence)
            {
                RenderFences.CloseFence(waitFence);
            }

            pass.AddRect(background, new Box(0, 0, target.Width, target.Height));
            var projection = new OutputProjection(scale);
            for (var i = 0; i < list.Count; i++)
            {
                DrawEntry(renderer, pass, list[i], clip: null, projection, luts);
            }

            return pass.Submit();
        }
        finally
        {
            ReturnList(list);
        }
    }

    private static readonly Stack<List<RenderEntry>> ListPool = new();

    private static List<RenderEntry> RentList()
    {
        if (!ListPool.TryPop(out var list))
        {
            list = [];
        }

        list.Clear();
        return list;
    }

    private static void ReturnList(List<RenderEntry> list)
    {
        list.Clear();
        ListPool.Push(list);
    }

    private const int IdleCaptureCommits = 120;

    private readonly List<SceneTransform> _idleCaptures = [];

    internal void ParkIdleCapture(SceneTransform node)
    {
        if (!_idleCaptures.Contains(node))
        {
            _idleCaptures.Add(node);
        }
    }

    private void AgeIdleCaptures()
    {
        for (var i = _idleCaptures.Count - 1; i >= 0; i--)
        {
            if (_idleCaptures[i].AgeIdleCapture(IdleCaptureCommits))
            {
                _idleCaptures.RemoveAt(i);
            }
        }
    }

    internal void PrepareCaptures(IRenderer renderer, List<RenderEntry> list, double scale)
    {
        AgeIdleCaptures();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Node is not SceneTransform { Deformer: not null } node)
            {
                continue;
            }

            if (node.ZeroCopySource() is not null)
            {
                continue;
            }

            var bounds = node.ChildBounds();
            if (!bounds.IsEmpty)
            {
                _ = node.EnsureCapture(renderer, this, bounds, scale);
            }
        }
    }

    internal bool RenderCapture(IRenderer renderer, SceneTransform node, MemoryBuffer target, in Box bounds, double scale)
    {
        var list = RentList();
        try
        {
            CollectTree(
                node, -bounds.X, -bounds.Y, Unclipped, RenderTransform.Identity, transformed: false, alpha: 1f, list,
                mirrorDepth: 0, InheritedFilter(node));
            PrepareCaptures(renderer, list, scale);
            var waitFence = GatherAcquireFences(list, out var ownsFence);
            var pass = renderer.BeginBufferPass(target, new RenderPassOptions { WaitFenceFd = waitFence });
            if (ownsFence)
            {
                RenderFences.CloseFence(waitFence);
            }

            pass.Clear(default);
            var projection = new OutputProjection(scale);
            for (var i = 0; i < list.Count; i++)
            {
                DrawEntry(renderer, pass, list[i], clip: null, projection, 0, 0, backdrops: false, luts: null);
            }

            return pass.Submit();
        }
        finally
        {
            ReturnList(list);
        }
    }

    private static void AccumulateFence(SceneNode node, ref int fence, ref bool owned)
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
                    RenderFences.CloseFence(fence);
                }

                fence = merged;
                owned = true;
                return;

            case SceneTree tree:
                foreach (var child in tree.Children)
                {
                    if (child.Enabled)
                    {
                        AccumulateFence(child, ref fence, ref owned);
                    }
                }

                return;
        }
    }

    internal static int GatherAcquireFences(List<RenderEntry> list, out bool owned)
    {
        var fence = -1;
        owned = false;
        for (var i = 0; i < list.Count; i++)
        {
            AccumulateFence(list[i].Node, ref fence, ref owned);
        }

        return fence;
    }

    private int GatherAcquireFences(out bool owned)
    {
        var fence = -1;
        owned = false;
        for (var i = 0; i < _renderList.Count; i++)
        {
            if (_renderList[i].Node is not SceneBuffer { AcquireFenceFd: >= 0 } node)
            {
                continue;
            }

            if (fence < 0)
            {
                fence = node.AcquireFenceFd;
                continue;
            }

            var merged = RenderFences.MergeSyncFiles(fence, node.AcquireFenceFd);
            if (merged < 0)
            {
                continue;
            }

            if (owned)
            {
                RenderFences.CloseFence(fence);
            }

            fence = merged;
            owned = true;
        }

        return fence;
    }

    internal static void DrawEntry(
        IRenderer renderer, IRenderPass pass, in RenderEntry entry, PixmanRegion32? clip, in OutputProjection projection,
        IColorLutTable? luts) =>
        DrawEntry(renderer, pass, entry, clip, projection, 0, 0, luts);

    internal static void DrawEntry(
        IRenderer renderer, IRenderPass pass, in RenderEntry entry, PixmanRegion32? clip, in OutputProjection projection, int offsetX, int offsetY,
        IColorLutTable? luts) =>
        DrawEntry(renderer, pass, entry, clip, projection, offsetX, offsetY, backdrops: true, luts);

    private static void DrawEntry(
        IRenderer renderer, IRenderPass pass, in RenderEntry entry, PixmanRegion32? clip, in OutputProjection projection, int offsetX, int offsetY,
        bool backdrops, IColorLutTable? luts)
    {
        var scale = projection.Scale;
        if (entry.Clip is { } clipBox)
        {
            var physical = projection.MapPixels(OutputScaling.ToPhysical(clipBox, scale).Translated(-offsetX, -offsetY));
            if (physical.IsEmpty)
            {
                return;
            }

            ClipScratch.Reset(new PixmanBox32(physical.X, physical.Y, physical.Right, physical.Bottom));
            if (clip is not null)
            {
                ClipScratch.IntersectWith(clip);
            }

            if (ClipScratch.IsEmpty)
            {
                return;
            }

            clip = ClipScratch;
        }

        switch (entry.Node)
        {
            case SceneMesh mesh:
                DrawSceneMesh(renderer, pass, mesh, entry, clip, projection, offsetX, offsetY);
                break;

            case SceneShader shader:
                DrawSceneShader(pass, shader, entry, clip, projection, offsetX, offsetY);
                break;

            case SceneTransform { Deformer: not null } deformed:
                DrawDeformed(renderer, pass, deformed, entry, clip, projection, offsetX, offsetY, backdrops, luts);
                break;

            case SceneRect rect:
                if (entry.Transformed)
                {
                    AddTransformedRect(pass, rect, entry, clip, projection, offsetX, offsetY);
                    break;
                }

                pass.AddRect(
                    ScaleColor(rect.Color, entry.Alpha),
                    projection.MapPixels(
                        OutputScaling.ToPhysical(new Box(entry.X, entry.Y, rect.Width, rect.Height), scale).Translated(-offsetX, -offsetY)),
                    clip);
                break;

            case SceneBuffer buffer:
                if (backdrops && buffer.HasActiveBackdrop && renderer.SupportsBackdropEffects)
                {
                    if (entry.Transformed)
                    {
                        AddTransformedBackdrop(pass, buffer, entry, clip, projection, offsetX, offsetY);
                    }
                    else
                    {
                        AddBackdrop(pass, buffer, entry, clip, projection, offsetX, offsetY);
                    }
                }

                if (buffer.GetTexture(renderer) is { } texture)
                {
                    var (width, height) = buffer.Size;
                    if (entry.Transformed)
                    {
                        var matrix = PhysicalTransform(entry.Transform, projection, offsetX, offsetY);
                        if (scale != 1.0)
                        {
                            matrix = RenderTransform.Multiply(matrix, RenderTransform.Scale(1.0 / scale, 1.0 / scale));
                        }

                        pass.AddTexture(texture, new TextureRenderOptions
                        {
                            SrcBox = buffer.SourceBox,
                            DstBox = OutputScaling.ToPhysical(new Box(entry.X, entry.Y, width, height), scale),
                            Transform = matrix,
                            Alpha = entry.Alpha,
                            Clip = clip,
                            Lut = luts?.LutFor(buffer),
                            ColorDescription = buffer.ColorDescription,
                            Shader = buffer.TextureShader,
                            Filter = entry.Filter,
                            Corners = buffer.Corners.Scale(scale),
                            Brightness = buffer.Brightness,
                            Saturation = buffer.Saturation,
                            Opaque = entry.Alpha >= 1f && buffer.IsOpaque && buffer.KeepsOpacity,
                        });
                    }
                    else
                    {
                        pass.AddTexture(texture, new TextureRenderOptions
                        {
                            SrcBox = buffer.SourceBox,
                            DstBox = (buffer.IsExactAt(scale)
                                ? buffer.DeviceBox(entry.X, entry.Y, scale)
                                : OutputScaling.ToPhysical(new Box(entry.X, entry.Y, width, height), scale)).Translated(-offsetX, -offsetY),
                            Transform = projection.MapsPixels ? projection.Matrix : RenderTransform.Identity,
                            Alpha = entry.Alpha,
                            Clip = clip,
                            Lut = luts?.LutFor(buffer),
                            ColorDescription = buffer.ColorDescription,
                            Shader = buffer.TextureShader,
                            Filter = entry.Filter,
                            Corners = buffer.Corners.Scale(scale),
                            Brightness = buffer.Brightness,
                            Saturation = buffer.Saturation,
                            Opaque = entry.Alpha >= 1f && buffer.IsOpaque && buffer.KeepsOpacity,
                        });
                    }
                }

                break;
        }
    }

    private static MeshVertex[] _meshScratch = [];

    private static MeshVertex[] _backdropMeshScratch = [];

    private static MeshVertex Local((double X, double Y) screen, in ClipVertex vertex, double originU, double originV) =>
        new((float)screen.X, (float)screen.Y, (float)(vertex.U - originU), (float)(vertex.V - originV), default);

    private static Span<MeshVertex> BackdropMeshScratch(int count)
    {
        if (_backdropMeshScratch.Length < count)
        {
            _backdropMeshScratch = new MeshVertex[Math.Max(count, _backdropMeshScratch.Length * 2)];
        }

        return _backdropMeshScratch.AsSpan(0, count);
    }

    private static Span<MeshVertex> MeshScratch(int count)
    {
        if (_meshScratch.Length < count)
        {
            _meshScratch = new MeshVertex[Math.Max(count, _meshScratch.Length * 2)];
        }

        return _meshScratch.AsSpan(0, count);
    }

    private static void DrawSceneMesh(
        IRenderer renderer, IRenderPass pass, SceneMesh mesh, in RenderEntry entry, PixmanRegion32? clip,
        in OutputProjection projection, int offsetX, int offsetY)
    {
        if (mesh.Source is not { } source || mesh.Bounds.IsEmpty)
        {
            return;
        }

        var count = source.VertexCount(mesh.Bounds);
        if (count <= 0 || count % 3 != 0)
        {
            return;
        }

        var vertices = MeshScratch(count);
        source.WriteVertices(mesh.Bounds, vertices);

        var full = RenderTransform.Multiply(
            PhysicalTransform(entry.Transformed ? entry.Transform : RenderTransform.Identity, projection, offsetX, offsetY),
            RenderTransform.Translation(entry.X, entry.Y));
        MapVertices(vertices, full, entry.Alpha);

        var texture = mesh.GetSpriteTexture(renderer);
        pass.AddMesh(texture, vertices, new MeshRenderOptions { Blend = mesh.Blend, Clip = clip });
    }

    private static void DrawSceneShader(
        IRenderPass pass, SceneShader node, in RenderEntry entry, PixmanRegion32? clip,
        in OutputProjection projection, int offsetX, int offsetY)
    {
        if (node.Shader is not { } shader || node.Bounds.IsEmpty || entry.Transformed)
        {
            return;
        }

        var dst = projection.MapPixels(OutputScaling
            .ToPhysical(node.Bounds.Translated(entry.X, entry.Y), projection.Scale)
            .Translated(-offsetX, -offsetY));
        if (dst.IsEmpty)
        {
            return;
        }

        pass.AddShader(shader, new ShaderRenderOptions { DstBox = dst, Alpha = entry.Alpha, Clip = clip });
    }

    private static void DrawDeformed(
        IRenderer renderer, IRenderPass pass, SceneTransform node, in RenderEntry entry, PixmanRegion32? clip,
        in OutputProjection projection, int offsetX, int offsetY, bool backdrops, IColorLutTable? luts)
    {
        if (node.Deformer is not { } deformer)
        {
            return;
        }

        var bounds = node.ChildBounds();
        if (bounds.IsEmpty)
        {
            return;
        }

        var count = deformer.VertexCount(bounds);
        if (count <= 0 || count % 3 != 0)
        {
            return;
        }

        var vertices = MeshScratch(count);
        deformer.WriteVertices(bounds, vertices);

        ITexture? texture;
        double uvOffsetX, uvOffsetY, uvScale;
        if (node.ZeroCopySource() is { } zeroCopy)
        {
            texture = zeroCopy.GetTexture(renderer);
            uvOffsetX = zeroCopy.X;
            uvOffsetY = zeroCopy.Y;
            uvScale = 1.0;
        }
        else
        {
            var (capture, captureBounds, captureScale) = node.Capture;
            if (capture is null)
            {
                return;
            }

            texture = node.GetCaptureTexture(renderer);
            uvOffsetX = captureBounds.X;
            uvOffsetY = captureBounds.Y;
            uvScale = captureScale;
        }

        if (texture is null)
        {
            return;
        }

        var full = RenderTransform.Multiply(
            PhysicalTransform(entry.Transformed ? entry.Transform : RenderTransform.Identity, projection, offsetX, offsetY),
            RenderTransform.Multiply(RenderTransform.Translation(entry.X, entry.Y), node.Matrix));
        if (backdrops && renderer.SupportsBackdropEffects)
        {
            AddDeformedBackdrops(pass, node, 0, 0, vertices, full, clip);
        }

        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            var (px, py) = full.Map(vertex.X, vertex.Y);
            vertices[i] = new MeshVertex(
                (float)px,
                (float)py,
                (float)((vertex.U - uvOffsetX) * uvScale),
                (float)((vertex.V - uvOffsetY) * uvScale),
                ScaleColor(vertex.Color, entry.Alpha));
        }

        pass.AddMesh(texture, vertices, new MeshRenderOptions { Clip = clip });
    }

    private static void AddDeformedBackdrops(
        IRenderPass pass, SceneTree tree, int x, int y, ReadOnlySpan<MeshVertex> vertices, in RenderTransform full,
        PixmanRegion32? clip)
    {
        Span<ClipVertex> polygon = stackalloc ClipVertex[9];
        Span<ClipVertex> spare = stackalloc ClipVertex[9];
        Span<(double X, double Y)> mapped = stackalloc (double X, double Y)[9];
        foreach (var child in tree.Children)
        {
            if (!child.Enabled)
            {
                continue;
            }

            switch (child)
            {
                case SceneBuffer { HasActiveBackdrop: true } buffer:
                {
                    var (width, height) = buffer.Size;
                    NodeScratch.Reset(new PixmanBox32(0, 0, width, height));
                    LocalScratch.Intersect(buffer.BackdropRegion!, NodeScratch);
                    if (LocalScratch.IsEmpty)
                    {
                        break;
                    }

                    var extents = LocalScratch.Extents;
                    var minU = x + child.X + extents.X1;
                    var minV = y + child.Y + extents.Y1;
                    var maxU = x + child.X + extents.X2;
                    var maxV = y + child.Y + extents.Y2;
                    var mesh = BackdropMeshScratch(vertices.Length * 5);
                    var meshCount = 0;
                    var originU = x + child.X;
                    var originV = y + child.Y;
                    BackdropScratch.Clear();
                    for (var i = 0; i + 2 < vertices.Length; i += 3)
                    {
                        var sampleMinU = Math.Min(vertices[i].U, Math.Min(vertices[i + 1].U, vertices[i + 2].U));
                        var sampleMaxU = Math.Max(vertices[i].U, Math.Max(vertices[i + 1].U, vertices[i + 2].U));
                        var sampleMinV = Math.Min(vertices[i].V, Math.Min(vertices[i + 1].V, vertices[i + 2].V));
                        var sampleMaxV = Math.Max(vertices[i].V, Math.Max(vertices[i + 1].V, vertices[i + 2].V));
                        if (sampleMaxU < minU || sampleMinU > maxU || sampleMaxV < minV || sampleMinV > maxV)
                        {
                            continue;
                        }

                        for (var k = 0; k < 3; k++)
                        {
                            var vertex = vertices[i + k];
                            polygon[k] = new ClipVertex(vertex.U, vertex.V, vertex.X, vertex.Y);
                        }

                        var count = ClipVertex.Clip(polygon, spare, 3, minU, minV, maxU, maxV);
                        if (count < 3)
                        {
                            continue;
                        }

                        var left = double.MaxValue;
                        var top = double.MaxValue;
                        var right = double.MinValue;
                        var bottom = double.MinValue;
                        for (var k = 0; k < count; k++)
                        {
                            mapped[k] = full.Map(polygon[k].X, polygon[k].Y);
                            left = Math.Min(left, mapped[k].X);
                            top = Math.Min(top, mapped[k].Y);
                            right = Math.Max(right, mapped[k].X);
                            bottom = Math.Max(bottom, mapped[k].Y);
                        }

                        for (var k = 1; k + 1 < count; k++)
                        {
                            mesh[meshCount++] = Local(mapped[0], polygon[0], originU, originV);
                            mesh[meshCount++] = Local(mapped[k], polygon[k], originU, originV);
                            mesh[meshCount++] = Local(mapped[k + 1], polygon[k + 1], originU, originV);
                        }

                        var boxLeft = (int)Math.Floor(left);
                        var boxTop = (int)Math.Floor(top);
                        var boxRight = (int)Math.Ceiling(right);
                        var boxBottom = (int)Math.Ceiling(bottom);
                        if (boxRight > boxLeft && boxBottom > boxTop)
                        {
                            BackdropScratch.UnionRect(BackdropScratch, boxLeft, boxTop, (uint)(boxRight - boxLeft), (uint)(boxBottom - boxTop));
                        }
                    }

                    var whole = BackdropScratch.Extents;
                    if (clip is not null)
                    {
                        BackdropScratch.IntersectWith(clip);
                    }

                    if (!BackdropScratch.IsEmpty)
                    {
                        var box = BackdropScratch.Extents;
                        pass.AddDeformedBackdropEffect(
                            buffer.BackdropEffect!,
                            new Box(box.X1, box.Y1, box.X2 - box.X1, box.Y2 - box.Y1),
                            new Box(whole.X1, whole.Y1, whole.X2 - whole.X1, whole.Y2 - whole.Y1),
                            BackdropScratch,
                            buffer.BackdropKey,
                            mesh[..meshCount]);
                    }

                    break;
                }

                case SceneTree subtree:
                    AddDeformedBackdrops(pass, subtree, x + child.X, y + child.Y, vertices, full, clip);
                    break;
            }
        }
    }

    private static void MapVertices(Span<MeshVertex> vertices, in RenderTransform full, float alpha)
    {
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            var (px, py) = full.Map(vertex.X, vertex.Y);
            vertices[i] = new MeshVertex((float)px, (float)py, vertex.U, vertex.V, ScaleColor(vertex.Color, alpha));
        }
    }

    private static RenderColor ScaleColor(in RenderColor color, float alpha) => alpha >= 1f
        ? color
        : new RenderColor(color.R * alpha, color.G * alpha, color.B * alpha, color.A * alpha);

    private static RenderTransform PhysicalTransform(in RenderTransform frame, in OutputProjection projection, int offsetX, int offsetY)
    {
        var scale = projection.Scale;
        var physical = scale == 1.0
            ? frame
            : RenderTransform.Multiply(RenderTransform.Scale(scale, scale), frame);
        var placed = offsetX == 0 && offsetY == 0
            ? physical
            : RenderTransform.Multiply(RenderTransform.Translation(-offsetX, -offsetY), physical);
        return projection.MapsPixels
            ? RenderTransform.Multiply(projection.Matrix, placed)
            : placed;
    }

    private static void AddTransformedRect(
        IRenderPass pass, SceneRect rect, in RenderEntry entry, PixmanRegion32? clip, in OutputProjection projection, int offsetX, int offsetY)
    {
        var transform = PhysicalTransform(entry.Transform, projection, offsetX, offsetY);
        var color = ScaleColor(rect.Color, entry.Alpha);
        var (x0, y0) = transform.Map(entry.X, entry.Y);
        var (x1, y1) = transform.Map(entry.X + rect.Width, entry.Y);
        var (x2, y2) = transform.Map(entry.X, entry.Y + rect.Height);
        var (x3, y3) = transform.Map(entry.X + rect.Width, entry.Y + rect.Height);
        Span<MeshVertex> corners =
        [
            new((float)x0, (float)y0, 0, 0, color),
            new((float)x1, (float)y1, 0, 0, color),
            new((float)x2, (float)y2, 0, 0, color),
            new((float)x1, (float)y1, 0, 0, color),
            new((float)x3, (float)y3, 0, 0, color),
            new((float)x2, (float)y2, 0, 0, color),
        ];
        pass.AddMesh(null, corners, new MeshRenderOptions { Clip = clip });
    }

    private static void AddBackdrop(
        IRenderPass pass, SceneBuffer buffer, in RenderEntry entry, PixmanRegion32? clip, in OutputProjection projection, int offsetX, int offsetY)
    {
        var (width, height) = buffer.Size;
        NodeScratch.Reset(new PixmanBox32(0, 0, width, height));
        LocalScratch.Intersect(buffer.BackdropRegion!, NodeScratch);

        BackdropScratch.Clear();
        foreach (var rect in RegionRects.Of(LocalScratch))
        {
            var physical = projection.MapPixels(OutputScaling
                .ToPhysical(new Box(entry.X + rect.X1, entry.Y + rect.Y1, rect.X2 - rect.X1, rect.Y2 - rect.Y1), projection.Scale)
                .Translated(-offsetX, -offsetY));
            if (!physical.IsEmpty)
            {
                BackdropScratch.UnionRect(BackdropScratch, physical.X, physical.Y, (uint)physical.Width, (uint)physical.Height);
            }
        }

        var whole = BackdropScratch.Extents;
        if (clip is not null)
        {
            BackdropScratch.IntersectWith(clip);
        }

        if (BackdropScratch.IsEmpty)
        {
            return;
        }

        var extents = BackdropScratch.Extents;
        pass.AddBackdropEffect(
            buffer.BackdropEffect!,
            new Box(extents.X1, extents.Y1, extents.X2 - extents.X1, extents.Y2 - extents.Y1),
            new Box(whole.X1, whole.Y1, whole.X2 - whole.X1, whole.Y2 - whole.Y1),
            BackdropScratch,
            buffer.BackdropKey);
    }

    private static void AddTransformedBackdrop(
        IRenderPass pass, SceneBuffer buffer, in RenderEntry entry, PixmanRegion32? clip, in OutputProjection projection, int offsetX, int offsetY)
    {
        var (width, height) = buffer.Size;
        NodeScratch.Reset(new PixmanBox32(0, 0, width, height));
        LocalScratch.Intersect(buffer.BackdropRegion!, NodeScratch);

        var transform = PhysicalTransform(entry.Transform, projection, offsetX, offsetY);
        BackdropScratch.Clear();
        foreach (var rect in RegionRects.Of(LocalScratch))
        {
            var local = new Box(entry.X + rect.X1, entry.Y + rect.Y1, rect.X2 - rect.X1, rect.Y2 - rect.Y1);
            if (transform.TryMapBounds(local, out var mapped) && !mapped.IsEmpty)
            {
                BackdropScratch.UnionRect(BackdropScratch, mapped.X, mapped.Y, (uint)mapped.Width, (uint)mapped.Height);
            }
        }

        var whole = BackdropScratch.Extents;
        if (clip is not null)
        {
            BackdropScratch.IntersectWith(clip);
        }

        if (BackdropScratch.IsEmpty)
        {
            return;
        }

        var extents = BackdropScratch.Extents;
        pass.AddBackdropEffect(
            buffer.BackdropEffect!,
            new Box(extents.X1, extents.Y1, extents.X2 - extents.X1, extents.Y2 - extents.Y1),
            new Box(whole.X1, whole.Y1, whole.X2 - whole.X1, whole.Y2 - whole.Y1),
            BackdropScratch,
            buffer.BackdropKey);
    }

    internal void CollectRenderList(List<RenderEntry> list, int offsetX, int offsetY)
    {
        list.Clear();
        CollectTree(Root, Root.X + offsetX, Root.Y + offsetY, Unclipped, list);
    }

    private static void CollectTree(SceneTree tree, int x, int y, Box clip, List<RenderEntry> list) =>
        CollectTree(tree, x, y, clip, RenderTransform.Identity, transformed: false, alpha: 1f, list, mirrorDepth: 0);

    private static void CollectTree(
        SceneTree tree, int x, int y, Box clip, in RenderTransform frame, bool transformed, float alpha,
        List<RenderEntry> list, int mirrorDepth, TextureFilter filter = TextureFilter.Linear)
    {
        filter = tree.Filter ?? filter;
        foreach (var child in tree.Children)
        {
            if (!child.Enabled)
            {
                continue;
            }

            var childX = x + child.X;
            var childY = y + child.Y;
            var childClip = clip;
            if (child.IsClipped)
            {
                var own = child.ClipBox.Translated(childX, childY);
                if (transformed)
                {
                    if (!frame.TryMapBounds(own, out own))
                    {
                        continue;
                    }
                }

                childClip = clip.Equals(Unclipped) ? own : clip.Intersect(own);
                if (childClip.IsEmpty)
                {
                    continue;
                }
            }

            switch (child)
            {
                case SceneTransform node:
                    if (node.Deformer is not null)
                    {
                        list.Add(new RenderEntry(
                            node, childX, childY, childClip.Equals(Unclipped) ? null : childClip,
                            transformed ? frame : RenderTransform.Identity, transformed, alpha * node.Alpha,
                            mirrorDepth > 0, node.Filter ?? filter));
                        break;
                    }

                    if (node.Matrix.IsIdentity)
                    {
                        CollectTree(node, childX, childY, childClip, frame, transformed, alpha * node.Alpha, list, mirrorDepth, filter);
                        break;
                    }

                    if (!node.Matrix.TryInvert(out _))
                    {
                        break;
                    }

                    var local = RenderTransform.Multiply(
                        RenderTransform.Multiply(RenderTransform.Translation(childX, childY), node.Matrix),
                        RenderTransform.Translation(-childX, -childY));
                    var childFrame = transformed ? RenderTransform.Multiply(frame, local) : local;
                    CollectTree(
                        node, childX, childY, childClip, childFrame, transformed: true, alpha * node.Alpha, list, mirrorDepth, filter);
                    break;

                case SceneMirror mirror:
                {
                    if (mirrorDepth >= MaxMirrorDepth || mirror.Source is not { IsDestroyed: false, Enabled: true } source)
                    {
                        break;
                    }

                    var box = new Box(childX, childY, mirror.Width, mirror.Height);
                    if (box.IsEmpty || (transformed && !frame.TryMapBounds(box, out box)))
                    {
                        break;
                    }

                    var mirrorClip = childClip.Equals(Unclipped) ? box : childClip.Intersect(box);
                    if (mirrorClip.IsEmpty)
                    {
                        break;
                    }

                    CollectTree(
                        source, childX + mirror.SourceX, childY + mirror.SourceY, mirrorClip, frame, transformed,
                        alpha, list, mirrorDepth + 1, filter);
                    break;
                }

                case SceneTree subtree:
                    CollectTree(subtree, childX, childY, childClip, frame, transformed, alpha * subtree.Alpha, list, mirrorDepth, filter);
                    break;

                default:
                    var entryClip = childClip;
                    if (child is SceneBuffer { VisibleBox: { } visible })
                    {
                        var own = visible.Translated(childX, childY);
                        if (transformed && !frame.TryMapBounds(own, out own))
                        {
                            break;
                        }

                        entryClip = entryClip.Equals(Unclipped) ? own : entryClip.Intersect(own);
                        if (entryClip.IsEmpty)
                        {
                            break;
                        }
                    }

                    list.Add(new RenderEntry(
                        child, childX, childY, entryClip.Equals(Unclipped) ? null : entryClip,
                        transformed ? frame : RenderTransform.Identity, transformed, alpha, mirrorDepth > 0, filter));
                    break;
            }
        }
    }

    private static TextureFilter InheritedFilter(SceneNode node)
    {
        for (var tree = node.Parent; tree is not null; tree = tree.Parent)
        {
            if (tree.Filter is { } filter)
            {
                return filter;
            }
        }

        return TextureFilter.Linear;
    }

    private static readonly Box Unclipped = new(int.MinValue / 4, int.MinValue / 4, int.MaxValue / 2, int.MaxValue / 2);

    private static readonly PixmanRegion32 ClipScratch = new();

    private static readonly PixmanRegion32 BackdropScratch = new();
    private static readonly PixmanRegion32 NodeScratch = new();
    private static readonly PixmanRegion32 LocalScratch = new();

    internal readonly record struct RenderEntry(
        SceneNode Node,
        int X,
        int Y,
        Box? Clip = null,
        RenderTransform Transform = default,
        bool Transformed = false,
        float Alpha = 1f,
        bool Mirrored = false,
        TextureFilter Filter = TextureFilter.Linear);
}
