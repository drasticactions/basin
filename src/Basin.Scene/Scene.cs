using Basin.Diagnostics;
using Pixman;

namespace Basin.Scene;

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

    public Scene()
    {
        Root = new SceneTree(null) { Owner = null! };
        Root.Owner = this;
    }

    public SceneTree Root { get; }

    public Func<IBuffer, ICrossDeviceConversion?>? CrossDeviceImport { get; set; }

    public event Action<SceneNode?, Box>? Damaged;

    public event Action? StructureChanged;

    public event Action? FrameRequested;

    internal void NotifyStructureChanged() => StructureChanged?.Invoke();

    internal void NotifyFrameRequested() => FrameRequested?.Invoke();

    public void SendFrameDone(uint timestampMs)
    {
        if (!HasFrameWork(Root))
        {
            return;
        }

        AllocationScope.Begin(warmup: 1);
        SendFrameDone(Root, timestampMs);
        AllocationScope.End();
    }

    private static bool HasFrameWork(SceneNode node)
    {
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

    private static void SendFrameDone(SceneNode node, uint timestampMs)
    {
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

    public bool Render(IRenderer renderer, IBuffer target, RenderColor background, double scale = 1.0) =>
        Render(renderer, target, new SceneRenderOptions { Background = background, Scale = scale });

    public bool Render(IRenderer renderer, IBuffer target, in SceneRenderOptions options)
    {
        var projection = options.Projection;
        _renderList.Clear();
        CollectTree(Root, Root.X - options.OriginX, Root.Y - options.OriginY, Unclipped, _renderList);
        PrepareCaptures(renderer, _renderList, projection.Scale);

        var targetBox = new Box(0, 0, target.Width, target.Height);

        var waitFence = GatherAcquireFences(_renderList, out var ownsFence);

        var pass = renderer.BeginBufferPass(
            target, new RenderPassOptions { WaitFenceFd = waitFence, ColorDescription = options.ColorDescription });
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
                var (contentX, contentY) = contentTree.ScenePosition;
                CollectTree(
                    contentTree, contentX - originX, contentY - originY, Unclipped, RenderTransform.Identity,
                    transformed: false, contentTree.Alpha, list, mirrorDepth: 0);
            }

            if (popups is SceneTree popupTree)
            {
                var clip = popupClip is { } box ? box.Translated(-originX, -originY) : Unclipped;
                if (!clip.IsEmpty)
                {
                    var (popupX, popupY) = popupTree.ScenePosition;
                    CollectTree(
                        popupTree, popupX - originX, popupY - originY, clip, RenderTransform.Identity,
                        transformed: false, popupTree.Alpha, list, mirrorDepth: 0);
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

    internal void PrepareCaptures(IRenderer renderer, List<RenderEntry> list, double scale)
    {
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

    internal unsafe bool RenderCapture(IRenderer renderer, SceneTransform node, MemoryBuffer target, in Box bounds, double scale)
    {
        if (!target.BeginDataAccess(BufferDataAccess.Write, out var view))
        {
            return false;
        }

        try
        {
            for (var y = 0; y < target.Height; y++)
            {
                new Span<byte>((void*)(view.Data + (y * view.Stride)), target.Width * 4).Clear();
            }
        }
        finally
        {
            target.EndDataAccess();
        }

        var list = RentList();
        try
        {
            CollectTree(
                node, -bounds.X, -bounds.Y, Unclipped, RenderTransform.Identity, transformed: false, alpha: 1f, list,
                mirrorDepth: 0);
            PrepareCaptures(renderer, list, scale);
            var waitFence = GatherAcquireFences(list, out var ownsFence);
            var pass = renderer.BeginBufferPass(target, new RenderPassOptions { WaitFenceFd = waitFence });
            if (ownsFence)
            {
                RenderFences.CloseFence(waitFence);
            }

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
                            Opaque = entry.Alpha >= 1f && buffer.IsOpaque && buffer.TextureShader is null,
                        });
                    }
                    else
                    {
                        pass.AddTexture(texture, new TextureRenderOptions
                        {
                            SrcBox = buffer.SourceBox,
                            DstBox = OutputScaling.ToPhysical(new Box(entry.X, entry.Y, width, height), scale).Translated(-offsetX, -offsetY),
                            Transform = projection.MapsPixels ? projection.Matrix : RenderTransform.Identity,
                            Alpha = entry.Alpha,
                            Clip = clip,
                            Lut = luts?.LutFor(buffer),
                            ColorDescription = buffer.ColorDescription,
                            Shader = buffer.TextureShader,
                            Opaque = entry.Alpha >= 1f && buffer.IsOpaque && buffer.TextureShader is null,
                        });
                    }
                }

                break;
        }
    }

    private static MeshVertex[] _meshScratch = [];

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

                        var (x0, y0) = full.Map(vertices[i].X, vertices[i].Y);
                        var (x1, y1) = full.Map(vertices[i + 1].X, vertices[i + 1].Y);
                        var (x2, y2) = full.Map(vertices[i + 2].X, vertices[i + 2].Y);
                        var left = (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)));
                        var top = (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)));
                        var right = (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2)));
                        var bottom = (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2)));
                        if (right > left && bottom > top)
                        {
                            BackdropScratch.UnionRect(BackdropScratch, left, top, (uint)(right - left), (uint)(bottom - top));
                        }
                    }

                    if (clip is not null)
                    {
                        BackdropScratch.IntersectWith(clip);
                    }

                    if (!BackdropScratch.IsEmpty)
                    {
                        var box = BackdropScratch.Extents;
                        pass.AddBackdropEffect(
                            buffer.BackdropEffect!,
                            new Box(box.X1, box.Y1, box.X2 - box.X1, box.Y2 - box.Y1),
                            BackdropScratch,
                            buffer.BackdropKey);
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
        List<RenderEntry> list, int mirrorDepth)
    {
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
                            mirrorDepth > 0));
                        break;
                    }

                    if (node.Matrix.IsIdentity)
                    {
                        CollectTree(node, childX, childY, childClip, frame, transformed, alpha * node.Alpha, list, mirrorDepth);
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
                        node, childX, childY, childClip, childFrame, transformed: true, alpha * node.Alpha, list, mirrorDepth);
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
                        alpha, list, mirrorDepth + 1);
                    break;
                }

                case SceneTree subtree:
                    CollectTree(subtree, childX, childY, childClip, frame, transformed, alpha * subtree.Alpha, list, mirrorDepth);
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
                        transformed ? frame : RenderTransform.Identity, transformed, alpha, mirrorDepth > 0));
                    break;
            }
        }
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
        bool Mirrored = false);
}
