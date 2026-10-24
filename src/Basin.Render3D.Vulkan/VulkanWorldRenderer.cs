using System.Numerics;
using Basin.Diagnostics;
using Basin.Render.Vulkan;
using Basin.Scene3D;
using Silk.NET.Vulkan;

namespace Basin.Render3D.Vulkan;

/// <summary>
/// The Vulkan 3D pass. It draws on the 2D renderer's <see cref="VulkanDevice"/> into dmabuf targets only, and draws the same image as the GL pass. Use it on the compositor thread only.
/// </summary>
public sealed unsafe class VulkanWorldRenderer : IWorldRenderer
{
    private const int MaxLights = 8;
    private const int MaxJoints = Skeleton.MaxJoints;
    private const int MaxLevels = 32;
    private const int FrameSlots = 3;
    private const long WarmupFrames = 3;
    private const long IdleFrames = 600;

    private static readonly Vector2 StandardSpecular = new(0.25f, 24);
    private static readonly Vector2 NoSpecular = new(0, 1);

    private static readonly Matrix4x4 ClipRemap = new(
        1, 0, 0, 0,
        0, -1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1);

    private readonly VulkanDevice _device;
    private readonly Vk _vk;
    private readonly VulkanWorldPipelines _pipelines;
    private readonly VulkanWorldFrame[] _frames = new VulkanWorldFrame[FrameSlots];
    private readonly Dictionary<IBuffer, VulkanWorldTarget> _targets = [];
    private readonly Dictionary<WorldMesh, VulkanWorldMesh> _meshes = [];
    private readonly Dictionary<SkinnedMesh, VulkanWorldMesh> _skinnedMeshes = [];
    private readonly Dictionary<WorldImage, VulkanWorldImage> _images = [];
    private readonly Dictionary<WorldWindow, VulkanWorldWindow> _windows = [];
    private readonly Dictionary<IBuffer, Action> _destroyHandlers = [];
    private readonly List<(ulong Point, VulkanWorldTarget Target)> _droppedTargets = [];
    private readonly List<(ulong Point, VulkanWorldImage Image)> _droppedImages = new(16);
    private readonly List<(ulong Point, VulkanWorldMesh Mesh)> _droppedMeshes = new(16);
    private readonly List<WorldMesh> _evict = [];
    private readonly List<ITexture> _foreign = new(32);
    private readonly VulkanWorldLevel[] _levels = new VulkanWorldLevel[MaxLevels];
    private readonly float[] _lightDistances = new float[64];
    private readonly int[] _lightOrder = new int[64];
    private readonly float[] _sortKeys = new float[64];
    private readonly int[] _sortItems = new int[64];
    private readonly VulkanWorldImage _white;
    private readonly Silk.NET.Vulkan.Extensions.KHR.KhrExternalSemaphoreFd? _semaphoreFd;
    private int _levelCount;
    private int _frameIndex;
    private long _frame;
    private bool _disposed;
    private CommandBuffer _commands;
    private VulkanWorldFrame? _current;
    private Pipeline _bound;
    private uint _stencilReference;
    private float _directMax = 1.2f;

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanWorldRenderer"/> class. It creates its pipelines, samplers and frame slots on <paramref name="device"/>.
    /// </summary>
    /// <param name="device">The Vulkan device of the 2D renderer.</param>
    /// <param name="anisotropy">The maximum anisotropic filtering for window and atlas textures. It applies only when the device enables <c>samplerAnisotropy</c>. This pass ignores <see cref="WorldRenderSettings.Anisotropy"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public VulkanWorldRenderer(VulkanDevice device, float anisotropy = 8)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _vk = device.Api;
        _pipelines = new VulkanWorldPipelines(device, anisotropy);
        for (var i = 0; i < _frames.Length; i++)
        {
            _frames[i] = new VulkanWorldFrame(device, _pipelines);
        }

        for (var i = 0; i < _levels.Length; i++)
        {
            _levels[i] = new VulkanWorldLevel();
        }

        if (device.EnabledExtensions.Contains("VK_KHR_external_semaphore_fd") &&
            _vk.TryGetDeviceExtension(device.Instance, device.Device, out Silk.NET.Vulkan.Extensions.KHR.KhrExternalSemaphoreFd semaphoreFd))
        {
            _semaphoreFd = semaphoreFd;
        }

        _white = Upload(new WorldImage(1, 1, [255, 255, 255, 255]));
        BasinCounters.Track();
    }

    /// <inheritdoc/>
    public string Name => "vulkan";

    /// <summary>
    /// Gets the Vulkan device that the pass draws on.
    /// </summary>
    public VulkanDevice Device => _device;

    /// <summary>
    /// Gets the number of mipmapped window copies that the last frame refreshed.
    /// </summary>
    public int CopiesRefreshed { get; private set; }

    /// <summary>
    /// Gets the number of window layers that the last frame sampled directly, with no mipmapped copy.
    /// </summary>
    public int DirectSamples { get; private set; }

    /// <summary>
    /// Records and submits the frame into <paramref name="target"/>. It waits for the target's readers and the sampled dmabufs' writers, and fences them after the submit.
    /// </summary>
    /// <param name="frame">The world, the view, the visible set and the settings for this output.</param>
    /// <param name="target">The dmabuf to draw into. The pass caches a framebuffer for it until it is destroyed.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    /// <exception cref="ObjectDisposedException">The renderer is disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="target"/> is not a dmabuf, or the device cannot import it.</exception>
    public bool Render(in WorldFrame frame, IBuffer target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (_frame < WarmupFrames)
        {
            return RenderCore(frame, target);
        }

        AllocationScope.Begin(region: "VulkanWorldFrame", forgiving: true);
        try
        {
            return RenderCore(frame, target);
        }
        finally
        {
            AllocationScope.End();
        }
    }

    /// <inheritdoc/>
    public void Forget(WorldWindow window)
    {
        if (_windows.Remove(window, out var state))
        {
            foreach (var copy in state.Copies)
            {
                if (copy is not null)
                {
                    _droppedImages.Add((copy.LastPoint, copy));
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = _vk.DeviceWaitIdle(_device.Device);
        foreach (var (buffer, target) in _targets)
        {
            if (_destroyHandlers.Remove(buffer, out var handler))
            {
                buffer.Destroyed -= handler;
            }

            target.Destroy(_device);
        }

        _targets.Clear();
        foreach (var mesh in _meshes.Values)
        {
            mesh.Destroy(_device);
        }

        foreach (var mesh in _skinnedMeshes.Values)
        {
            mesh.Destroy(_device);
        }

        foreach (var image in _images.Values)
        {
            image.Destroy(_device);
        }

        foreach (var state in _windows.Values)
        {
            foreach (var copy in state.Copies)
            {
                copy?.Destroy(_device);
            }
        }

        foreach (var (_, target) in _droppedTargets)
        {
            target.Destroy(_device);
        }

        foreach (var (_, image) in _droppedImages)
        {
            image.Destroy(_device);
        }

        foreach (var (_, mesh) in _droppedMeshes)
        {
            mesh.Destroy(_device);
        }

        _white.Destroy(_device);
        foreach (var frame in _frames)
        {
            frame.Dispose();
        }

        _pipelines.Dispose();
        BasinCounters.Untrack();
    }

    private bool RenderCore(in WorldFrame frame, IBuffer target)
    {
        _frame++;
        _directMax = frame.Settings.DirectSampleMax;
        CopiesRefreshed = 0;
        DirectSamples = 0;
        var entry = TargetFor(target);
        var slot = AcquireFrame();
        _current = slot;
        _device.Staging.Recycle(_device.CompletedPoint);
        ReleaseDropped();

        _levelCount = 0;
        Plan(frame, frame.View, frame.Visible, 0, frame.Settings);

        WaitForReaders(entry.Attributes);
        _commands = _device.BeginCommands();
        entry.Color.RecordForeignAcquire(_commands);
        Prepare(frame, entry, target.Width, target.Height);

        ClearValue* clears = stackalloc ClearValue[2];
        var clear = frame.World.ClearColor;
        clears[0] = new ClearValue(new ClearColorValue(clear.X, clear.Y, clear.Z, 1));
        clears[1] = new ClearValue(depthStencil: new ClearDepthStencilValue(1, 0));
        var area = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)target.Width, (uint)target.Height));
        var begin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _pipelines.MainPass,
            Framebuffer = entry.Framebuffer,
            RenderArea = area,
            ClearValueCount = 2,
            PClearValues = clears,
        };
        _vk.CmdBeginRenderPass(_commands, in begin, SubpassContents.Inline);
        var viewport = new Viewport(0, 0, target.Width, target.Height, 0, 1);
        _vk.CmdSetViewport(_commands, 0, 1, in viewport);
        _vk.CmdSetScissor(_commands, 0, 1, in area);
        _bound = default;
        _stencilReference = uint.MaxValue;

        DrawLevel(frame, 0);

        _vk.CmdEndRenderPass(_commands);
        for (var i = 0; i < _foreign.Count; i++)
        {
            if (VulkanTextures.TryOpen(_device, _foreign[i], default, out var view))
            {
                _device.ReleaseImported(_commands, view.Image);
            }
        }

        entry.Color.RecordForeignRelease(_commands);
        var fence = _device.SubmitExportable(_commands, slot.Waits, out var point);
        _commands = default;
        _device.Staging.MarkSubmitted(point);
        slot.Point = point;
        entry.LastPoint = point;
        if (fence >= 0)
        {
            RenderFences.PublishFenceTo(entry.Attributes, forWrite: true, fence);
            for (var i = 0; i < _foreign.Count; i++)
            {
                if (VulkanTextures.TryGetDmabuf(_foreign[i], out var attributes))
                {
                    RenderFences.PublishFenceTo(attributes, forWrite: false, fence);
                }
            }

            RenderFences.CloseFence(fence);
        }
        else
        {
            _device.WaitFor(point);
        }

        _foreign.Clear();
        _current = null;
        EvictIdle(point);
        return true;
    }

    private int Plan(in WorldFrame frame, in WorldView view, WorldVisibleSet set, int depth, WorldRenderSettings settings)
    {
        var index = _levelCount++;
        var level = _levels[index];
        level.View = view;
        level.Set = set;
        level.Depth = depth;
        level.Portals.Clear();
        WriteScene(frame, level, settings.MaxLights);
        if (depth >= settings.PortalDepth)
        {
            return index;
        }

        for (var p = 0; p < set.Portals.Count; p++)
        {
            var portal = set.Portals[p];
            if (portal.Target is not { } target || _levelCount >= MaxLevels ||
                Vector3.Dot(view.Eye - portal.Center, portal.Normal) <= 0.01f)
            {
                continue;
            }

            var inner = view.Through(portal.TransferTo(target), target.Plane);
            var child = _levels[_levelCount];
            child.OwnSet.Clear();
            WorldVisibility.Collect(frame.World, inner, settings.RoomDepth, child.OwnSet);
            for (var i = 0; i < child.OwnSet.Surfaces.Count; i++)
            {
                if (!frame.Visible.ContainsSurface(child.OwnSet.Surfaces[i]))
                {
                    frame.Visible.Surfaces.Add(child.OwnSet.Surfaces[i]);
                }
            }

            var childIndex = Plan(frame, inner, child.OwnSet, depth + 1, settings);
            level.Portals.Add((portal, childIndex));
        }

        return index;
    }

    private void WriteScene(in WorldFrame frame, VulkanWorldLevel level, int maxLights)
    {
        var buffer = _current!.Reserve(VulkanWorldPipelines.SceneSize, _pipelines.SceneStride, out var offset);
        level.SceneBuffer = buffer;
        level.SceneOffset = offset;
        var data = (float*)(buffer.Mapped + offset);
        var ambient = frame.World.Ambient;
        var eye = level.View.Eye;
        data[0] = ambient.X;
        data[1] = ambient.Y;
        data[2] = ambient.Z;
        data[3] = 1;
        data[4] = eye.X;
        data[5] = eye.Y;
        data[6] = eye.Z;
        data[7] = 1;
        var set = level.Set;
        var count = 0;
        for (var r = 0; r < set.Rooms.Count; r++)
        {
            var lights = set.Rooms[r].Lights;
            for (var l = 0; l < lights.Count && count < _lightOrder.Length; l++)
            {
                _lightDistances[count] = Vector3.DistanceSquared(lights[l].Position, eye);
                _lightOrder[count] = (r << 16) | l;
                count++;
            }
        }

        Array.Sort(_lightDistances, _lightOrder, 0, count);
        var used = Math.Min(count, Math.Clamp(maxLights, 0, MaxLights));
        ((int*)data)[8] = used;
        ((int*)data)[9] = 0;
        ((int*)data)[10] = 0;
        ((int*)data)[11] = 0;
        var positions = data + 12;
        var colors = data + 12 + (MaxLights * 4);
        for (var i = 0; i < MaxLights; i++)
        {
            if (i >= used)
            {
                positions[(i * 4) + 3] = 1;
                continue;
            }

            var light = set.Rooms[_lightOrder[i] >> 16].Lights[_lightOrder[i] & 0xFFFF];
            var intensity = light.IntensityAt(frame.Tick.TargetPresentNanos);
            positions[(i * 4) + 0] = light.Position.X;
            positions[(i * 4) + 1] = light.Position.Y;
            positions[(i * 4) + 2] = light.Position.Z;
            positions[(i * 4) + 3] = MathF.Max(light.Range, 0.01f);
            colors[(i * 4) + 0] = light.Color.X * intensity;
            colors[(i * 4) + 1] = light.Color.Y * intensity;
            colors[(i * 4) + 2] = light.Color.Z * intensity;
            colors[(i * 4) + 3] = 1;
        }
    }

    private void Prepare(in WorldFrame frame, VulkanWorldTarget entry, int width, int height)
    {
        for (var l = 0; l < _levelCount; l++)
        {
            var level = _levels[l];
            var set = level.Set;
            for (var r = 0; r < set.Rooms.Count; r++)
            {
                EnsureImage(set.Rooms[r].Structure?.Image);
            }

            for (var p = 0; p < set.Props.Count; p++)
            {
                EnsureImage(set.Props[p].Mesh.Image);
            }

            for (var w = 0; w < set.Windows.Count; w++)
            {
                PrepareWindow(level.View, set.Windows[w], width, height);
            }
        }

        if (frame.World.Avatar is { } avatar)
        {
            EnsureImage(avatar.Mesh.Image);
            var attachments = avatar.Attachments;
            for (var a = 0; a < attachments.Count; a++)
            {
                EnsureImage(attachments[a].Mesh.Image);
            }
        }
    }

    private void PrepareWindow(in WorldView view, WorldWindow window, int width, int height)
    {
        if (!_windows.TryGetValue(window, out var state))
        {
            AllocationScope.Pause();
            state = new VulkanWorldWindow();
            _windows[window] = state;
            AllocationScope.Resume();
        }

        if (state.PreparedFrame == _frame)
        {
            return;
        }

        state.PreparedFrame = _frame;
        var layers = window.Layers;
        if (state.Views.Length < layers.Length)
        {
            AllocationScope.Pause();
            state.Grow(layers.Length);
            AllocationScope.Resume();
        }

        var metersX = window.MetersPerPixel;
        var metersY = window.SurfaceHeight > 0 ? window.Height / window.SurfaceHeight : 0;
        var model = window.Model;
        for (var l = 0; l < layers.Length; l++)
        {
            state.Ready[l] = false;
            var layer = layers[l];
            if (layer.Texture is not { } texture || layer.Width <= 0 || layer.Height <= 0 ||
                !VulkanTextures.TryOpen(_device, texture, _commands, out var opened))
            {
                continue;
            }

            if (opened.Foreign && !_foreign.Contains(texture))
            {
                _foreign.Add(texture);
                _device.AcquireImported(_commands, opened.Image);
                if (VulkanTextures.TryGetDmabuf(texture, out var attributes))
                {
                    WaitForWriters(attributes);
                }
            }

            var source = layer.Source.Width > 0 && layer.Source.Height > 0
                ? layer.Source
                : new FBox(0, 0, opened.Width, opened.Height);
            state.UvRects[l] = new Vector4(
                (float)(source.X / opened.Width), (float)(source.Y / opened.Height),
                (float)(source.Width / opened.Width), (float)(source.Height / opened.Height));
            state.ForceOpaque[l] = !opened.HasAlpha || layer.Opaque;

            var centerX = ((layer.X + (layer.Width * 0.5f)) * metersX) - (window.Width * 0.5f);
            var centerY = (window.Height * 0.5f) - ((layer.Y + (layer.Height * 0.5f)) * metersY);
            var quad = Matrix4x4.CreateScale(layer.Width * metersX, layer.Height * metersY, 1) *
                Matrix4x4.CreateTranslation(centerX, centerY, 0) * model;
            if (TexelRatio(view, quad, source, width, height) <= _directMax)
            {
                state.Views[l] = opened.View;
                state.Samplers[l] = _pipelines.DirectSampler;
                state.Ready[l] = true;
                DirectSamples++;
                continue;
            }

            var copy = EnsureCopy(state, l, layer, texture, opened);
            state.Views[l] = copy.View;
            state.Samplers[l] = _pipelines.WindowSampler;
            state.Ready[l] = true;
        }

        PreparePlaque(window, state);
    }

    private void PreparePlaque(WorldWindow window, VulkanWorldWindow state)
    {
        state.PlaqueReady = false;
        if (window.ShownFrame is not { } frame)
        {
            return;
        }

        EnsureImage(frame.Moulding?.Image);
        if (!frame.HasPlaque || frame.PlaqueTexture is not { } texture ||
            !VulkanTextures.TryOpen(_device, texture, _commands, out var opened) || opened.Width <= 0 || opened.Height <= 0)
        {
            return;
        }

        if (opened.Foreign && !_foreign.Contains(texture))
        {
            _foreign.Add(texture);
            _device.AcquireImported(_commands, opened.Image);
            if (VulkanTextures.TryGetDmabuf(texture, out var attributes))
            {
                WaitForWriters(attributes);
            }
        }

        var source = frame.PlaqueSource.Width > 0 && frame.PlaqueSource.Height > 0
            ? frame.PlaqueSource
            : new FBox(0, 0, opened.Width, opened.Height);
        state.PlaqueUv = new Vector4(
            (float)(source.X / opened.Width), (float)(source.Y / opened.Height),
            (float)(source.Width / opened.Width), (float)(source.Height / opened.Height));
        state.PlaqueOpaque = !opened.HasAlpha;
        state.PlaqueView = opened.View;
        state.PlaqueReady = true;
    }

    private VulkanWorldImage EnsureCopy(VulkanWorldWindow state, int index, in WorldSurfaceLayer layer, ITexture texture, in VulkanTextureView opened)
    {
        var copy = state.Copies[index];
        if (copy is not null && (copy.Width != opened.Width || copy.Height != opened.Height))
        {
            _droppedImages.Add((copy.LastPoint, copy));
            copy = null;
        }

        if (copy is null)
        {
            AllocationScope.Pause();
            copy = VulkanWorldImage.Create(_device, opened.Width, opened.Height, VulkanWorldPipelines.ColorFormat, ImageUsageFlags.ColorAttachmentBit);
            var levelZero = copy.LevelZero;
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _pipelines.CopyPass,
                AttachmentCount = 1,
                PAttachments = &levelZero,
                Width = (uint)opened.Width,
                Height = (uint)opened.Height,
                Layers = 1,
            };
            VulkanDevice.Check(_vk.CreateFramebuffer(_device.Device, in framebufferInfo, null, out var framebuffer), "vkCreateFramebuffer(world copy)");
            copy.Framebuffer = framebuffer;
            state.Copies[index] = copy;
            AllocationScope.Resume();
        }

        if (ReferenceEquals(copy.Source, texture) && copy.Version == layer.ContentVersion)
        {
            return copy;
        }

        var area = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)copy.Width, (uint)copy.Height));
        var begin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _pipelines.CopyPass,
            Framebuffer = copy.Framebuffer,
            RenderArea = area,
        };
        _vk.CmdBeginRenderPass(_commands, in begin, SubpassContents.Inline);
        var viewport = new Viewport(0, 0, copy.Width, copy.Height, 0, 1);
        _vk.CmdSetViewport(_commands, 0, 1, in viewport);
        _vk.CmdSetScissor(_commands, 0, 1, in area);
        _vk.CmdBindPipeline(_commands, PipelineBindPoint.Graphics, _pipelines.CopyPipeline);
        var set = _current!.CopySet(opened.View, _pipelines.DirectSampler);
        _vk.CmdBindDescriptorSets(_commands, PipelineBindPoint.Graphics, _pipelines.CopyLayout, 0, 1, &set, 0, null);
        float* push = stackalloc float[8];
        push[0] = 0;
        push[1] = 0;
        push[2] = 1;
        push[3] = 1;
        push[4] = opened.HasAlpha ? 0 : 1;
        push[5] = 0;
        push[6] = 0;
        push[7] = 0;
        _vk.CmdPushConstants(_commands, _pipelines.CopyLayout, ShaderStageFlags.FragmentBit, 0, 32, (void*)push);
        _vk.CmdDraw(_commands, 4, 1, 0, 0);
        _vk.CmdEndRenderPass(_commands);
        copy.RecordMips(_vk, _commands, ImageLayout.ColorAttachmentOptimal);
        copy.Source = texture;
        copy.Version = layer.ContentVersion;
        CopiesRefreshed++;
        return copy;
    }

    private void DrawLevel(in WorldFrame frame, int index)
    {
        var level = _levels[index];
        var set = level.Set;
        var view = level.View;
        var world = frame.World;
        var viewProjection = view.ViewProjection * ClipRemap;
        var eye = view.Eye;
        SetStencil((uint)level.Depth);

        for (var i = 0; i < set.Rooms.Count; i++)
        {
            if (set.Rooms[i].Structure is { } structure)
            {
                DrawMesh(level, VulkanWorldPipeline.LitOpaque, structure, Matrix4x4.Identity, viewProjection, Vector4.One, true, Vector3.Zero);
            }
        }

        for (var i = 0; i < set.Props.Count; i++)
        {
            var prop = set.Props[i];
            DrawMesh(level, VulkanWorldPipeline.LitOpaque, prop.Mesh, prop.Model, viewProjection, Vector4.One, true, Vector3.Zero);
        }

        var translucent = 0;
        for (var i = 0; i < set.Windows.Count; i++)
        {
            var window = set.Windows[i];
            if (window.IsTranslucent)
            {
                if (translucent < _sortItems.Length)
                {
                    _sortItems[translucent] = i;
                    _sortKeys[translucent] = -Vector3.DistanceSquared(window.Position, eye);
                    translucent++;
                }

                continue;
            }

            DrawWindow(level, window, viewProjection, VulkanWorldPipeline.LitOpaque);
        }

        for (var r = 0; r < set.Rooms.Count; r++)
        {
            var lights = set.Rooms[r].Lights;
            for (var l = 0; l < lights.Count; l++)
            {
                var light = lights[l];
                var glow = light.Color * MathF.Min(light.IntensityAt(frame.Tick.TargetPresentNanos), 1.5f);
                var model = Matrix4x4.CreateScale(0.06f) * Matrix4x4.CreateTranslation(light.Position);
                DrawMesh(level, VulkanWorldPipeline.LitOpaque, WorldPrimitives.Flame, model, viewProjection, new Vector4(0, 0, 0, 1), false, glow);
            }
        }

        if (level.Depth > 0 && world.Avatar is { Visible: true } avatar)
        {
            DrawAvatar(level, avatar, viewProjection);
        }

        for (var p = 0; p < level.Portals.Count; p++)
        {
            var (portal, child) = level.Portals[p];
            var model = Matrix4x4.CreateScale(portal.Width, portal.Height, 1) * portal.Frame;
            var mvp = model * viewProjection;
            SetStencil((uint)level.Depth);
            DrawFlat(level, VulkanWorldPipeline.StencilIncrement, mvp, false, Vector4.Zero);
            SetStencil((uint)level.Depth + 1);
            DrawFlat(level, VulkanWorldPipeline.StencilReset, mvp, true, new Vector4(world.ClearColor, 1));
            DrawLevel(frame, child);
            SetStencil((uint)level.Depth + 1);
            DrawFlat(level, VulkanWorldPipeline.StencilDecrement, mvp, false, Vector4.Zero);
            SetStencil((uint)level.Depth);
        }

        if (translucent > 0)
        {
            Array.Sort(_sortKeys, _sortItems, 0, translucent);
            for (var i = 0; i < translucent; i++)
            {
                DrawWindow(level, set.Windows[_sortItems[i]], viewProjection, VulkanWorldPipeline.LitTranslucent);
            }
        }
    }

    private void DrawWindow(VulkanWorldLevel level, WorldWindow window, in Matrix4x4 viewProjection, VulkanWorldPipeline pipeline)
    {
        var model = window.Model;
        var opacity = window.Opacity;
        _windows.TryGetValue(window, out var state);
        if (window.ShownFrame is { } frame)
        {
            DrawFrame(level, window, frame, state, viewProjection, pipeline);
        }
        else
        {
            var slab = Matrix4x4.CreateScale(window.Width, window.Height, window.Depth) * model;
            var edge = window.Focused ? window.FocusColor : window.EdgeColor;
            if (window.Flash > 0)
            {
                edge = Vector4.Lerp(edge, Vector4.One, Math.Clamp(window.Flash, 0, 1));
            }

            var glow = window.Focused ? new Vector3(edge.X, edge.Y, edge.Z) * 0.35f : Vector3.Zero;
            DrawMesh(level, pipeline, WorldPrimitives.SlabEdges, slab, viewProjection, edge with { W = opacity }, true, glow);
            DrawMesh(level, pipeline, WorldPrimitives.SlabBack, slab, viewProjection, new Vector4(0.08f, 0.08f, 0.09f, opacity), true, Vector3.Zero);
        }

        if (state is null)
        {
            return;
        }

        var layers = window.Layers;
        var metersX = window.MetersPerPixel;
        var metersY = window.SurfaceHeight > 0 ? window.Height / window.SurfaceHeight : 0;
        for (var l = 0; l < layers.Length && l < state.Ready.Length; l++)
        {
            if (!state.Ready[l])
            {
                continue;
            }

            var layer = layers[l];
            var centerX = ((layer.X + (layer.Width * 0.5f)) * metersX) - (window.Width * 0.5f);
            var centerY = (window.Height * 0.5f) - ((layer.Y + (layer.Height * 0.5f)) * metersY);
            var quad = Matrix4x4.CreateScale(layer.Width * metersX, layer.Height * metersY, 1) *
                Matrix4x4.CreateTranslation(centerX, centerY, window.LayerDepth(layer) + 0.0001f) * model;
            var tint = new Vector4(1, 1, 1, opacity * window.CanvasOpacity);
            Draw(
                level, pipeline, MeshFor(WorldPrimitives.Quad), quad, viewProjection, tint, state.UvRects[l],
                new Vector4(window.Focused ? 0 : 1, 1, state.ForceOpaque[l] ? 1 : 0, 0), Vector3.Zero,
                state.Views[l], state.Samplers[l], null, StandardSpecular);
        }
    }

    private void DrawFrame(
        VulkanWorldLevel level, WorldWindow window, WorldWindowFrame frame, VulkanWorldWindow? state, in Matrix4x4 viewProjection,
        VulkanWorldPipeline pipeline)
    {
        WorldFrameShading.Frame(window, frame, out var albedo, out var emissive);
        var material = frame.Material;
        if (frame.Moulding is { } moulding)
        {
            DrawMesh(level, pipeline, moulding, window.Model, viewProjection, albedo, true, emissive,
                new Vector2(material.SpecularStrength, material.Shininess));
        }

        DrawMesh(level, pipeline, WorldPrimitives.SlabBack, WorldFrameShading.Back(window, frame), viewProjection,
            WorldFrameShading.BackColor with { W = window.Opacity }, true, Vector3.Zero, NoSpecular);
        if (!frame.HasPlaque)
        {
            return;
        }

        var plaque = frame.PlaqueMaterial;
        var tint = plaque.Albedo with { W = plaque.Albedo.W * window.Opacity };
        var specular = new Vector2(plaque.SpecularStrength, plaque.Shininess);
        DrawMesh(level, pipeline, WorldPrimitives.SlabEdges, WorldFrameShading.PlaqueBody(window, frame), viewProjection, tint, true, Vector3.Zero, specular);
        var face = WorldFrameShading.PlaqueFace(window, frame);
        if (state is { PlaqueReady: true })
        {
            Draw(level, pipeline, MeshFor(WorldPrimitives.Quad), face, viewProjection, new Vector4(1, 1, 1, window.Opacity), state.PlaqueUv,
                new Vector4(1, 1, state.PlaqueOpaque ? 1 : 0, 0), Vector3.Zero, state.PlaqueView, _pipelines.DirectSampler, null, specular);
            return;
        }

        DrawMesh(level, pipeline, WorldPrimitives.Quad, face, viewProjection, tint, true, Vector3.Zero, specular);
    }

    private void DrawAvatar(VulkanWorldLevel level, WorldAvatar avatar, in Matrix4x4 viewProjection)
    {
        if (!_skinnedMeshes.TryGetValue(avatar.Mesh, out var mesh))
        {
            AllocationScope.Pause();
            mesh = VulkanWorldMesh.Create(_device, avatar.Mesh);
            _skinnedMeshes[avatar.Mesh] = mesh;
            AllocationScope.Resume();
        }

        var model = avatar.Model;
        var image = ImageFor(avatar.Mesh.Image);
        Draw(level, VulkanWorldPipeline.Skinned, mesh, model, viewProjection, Vector4.One, new Vector4(0, 0, 1, 1),
            new Vector4(1, avatar.Mesh.Image is null ? 0 : 1, 0, 0), Vector3.Zero, image.View, _pipelines.AtlasSampler, avatar.Pose.Palette,
            StandardSpecular);
        var attachments = avatar.Attachments;
        for (var a = 0; a < attachments.Count; a++)
        {
            var attachment = attachments[a];
            if (attachment.Visible)
            {
                DrawMesh(level, VulkanWorldPipeline.LitTwoSided, attachment.Mesh, avatar.Pose.World[attachment.Node] * model, viewProjection, Vector4.One, true, Vector3.Zero);
            }
        }
    }

    private void DrawMesh(
        VulkanWorldLevel level, VulkanWorldPipeline pipeline, WorldMesh mesh, in Matrix4x4 model, in Matrix4x4 viewProjection,
        Vector4 tint, bool lit, Vector3 emissive) =>
        DrawMesh(level, pipeline, mesh, model, viewProjection, tint, lit, emissive, StandardSpecular);

    private void DrawMesh(
        VulkanWorldLevel level, VulkanWorldPipeline pipeline, WorldMesh mesh, in Matrix4x4 model, in Matrix4x4 viewProjection,
        Vector4 tint, bool lit, Vector3 emissive, Vector2 specular)
    {
        var image = ImageFor(mesh.Image);
        Draw(level, pipeline, MeshFor(mesh), model, viewProjection, tint, new Vector4(0, 0, 1, 1),
            new Vector4(lit ? 1 : 0, mesh.Image is null ? 0 : 1, 0, 0), emissive, image.View, _pipelines.AtlasSampler, null, specular);
    }

    private void DrawFlat(VulkanWorldLevel level, VulkanWorldPipeline pipeline, in Matrix4x4 mvp, bool far, Vector4 color) =>
        Draw(level, pipeline, MeshFor(WorldPrimitives.Quad), Matrix4x4.Identity, mvp, color, new Vector4(0, 0, 1, 1),
            new Vector4(0, 0, 0, far ? 1 : 0), Vector3.Zero, _white.View, _pipelines.AtlasSampler, null, NoSpecular);

    private void Draw(
        VulkanWorldLevel level, VulkanWorldPipeline pipeline, VulkanWorldMesh mesh, in Matrix4x4 model, in Matrix4x4 viewProjection,
        Vector4 tint, Vector4 uvRect, Vector4 flags, Vector3 emissive, ImageView view, Sampler sampler, Matrix4x4[]? palette,
        Vector2 specular)
    {
        var frame = _current!;
        var drawBuffer = frame.Reserve(VulkanWorldPipelines.DrawSize, _pipelines.DrawStride, out var drawOffset);
        var data = (float*)(drawBuffer.Mapped + drawOffset);
        var mvp = model * viewProjection;
        *(Matrix4x4*)data = mvp;
        *(Matrix4x4*)(data + 16) = model;
        *(Vector4*)(data + 32) = tint;
        *(Vector4*)(data + 36) = uvRect;
        *(Vector4*)(data + 40) = new Vector4(emissive, 0);
        *(Vector4*)(data + 44) = flags;
        *(Vector4*)(data + 48) = new Vector4(specular, 0, 0);

        var jointsBuffer = drawBuffer;
        var jointsOffset = drawOffset;
        if (palette is not null)
        {
            jointsBuffer = frame.Reserve(VulkanWorldPipelines.JointsSize, _pipelines.JointsStride, out jointsOffset);
            var count = Math.Min(palette.Length, MaxJoints);
            fixed (Matrix4x4* source = palette)
            {
                System.Buffer.MemoryCopy(source, jointsBuffer.Mapped + jointsOffset, VulkanWorldPipelines.JointsSize, count * sizeof(Matrix4x4));
            }

            if (!ReferenceEquals(jointsBuffer, drawBuffer))
            {
                drawBuffer = frame.Reserve(VulkanWorldPipelines.DrawSize, _pipelines.DrawStride, out drawOffset);
                System.Buffer.MemoryCopy(data, drawBuffer.Mapped + drawOffset, VulkanWorldPipelines.DrawSize, VulkanWorldPipelines.DrawSize);
            }
        }

        var sceneBuffer = level.SceneBuffer!;
        var key = new VulkanWorldSetKey(view.Handle, sampler.Handle, drawBuffer.Handle.Handle, sceneBuffer.Handle.Handle, jointsBuffer.Handle.Handle);
        var set = frame.SetFor(key, view, sampler, drawBuffer, sceneBuffer, jointsBuffer);
        var handle = _pipelines[pipeline];
        if (_bound.Handle != handle.Handle)
        {
            _vk.CmdBindPipeline(_commands, PipelineBindPoint.Graphics, handle);
            _bound = handle;
        }

        uint* offsets = stackalloc uint[3];
        offsets[0] = (uint)drawOffset;
        offsets[1] = (uint)level.SceneOffset;
        offsets[2] = (uint)jointsOffset;
        _vk.CmdBindDescriptorSets(_commands, PipelineBindPoint.Graphics, _pipelines.Layout, 0, 1, &set, 3, offsets);
        mesh.LastUsed = _frame;
        mesh.Draw(_vk, _commands);
    }

    private void SetStencil(uint reference)
    {
        if (_stencilReference != reference)
        {
            _vk.CmdSetStencilReference(_commands, StencilFaceFlags.FrontAndBack, reference);
            _stencilReference = reference;
        }
    }

    private static float TexelRatio(in WorldView view, in Matrix4x4 quad, in FBox source, int width, int height)
    {
        var clip = quad * view.ViewProjection;
        if (!Screen(clip, new Vector3(-0.5f, 0.5f, 0), width, height, out var a) ||
            !Screen(clip, new Vector3(0.5f, 0.5f, 0), width, height, out var b) ||
            !Screen(clip, new Vector3(-0.5f, -0.5f, 0), width, height, out var c))
        {
            return float.MaxValue;
        }

        var across = Vector2.Distance(a, b);
        var down = Vector2.Distance(a, c);
        var ratioX = across > 0.5f ? (float)source.Width / across : float.MaxValue;
        var ratioY = down > 0.5f ? (float)source.Height / down : float.MaxValue;
        return MathF.Max(ratioX, ratioY);

        static bool Screen(in Matrix4x4 clip, Vector3 local, int width, int height, out Vector2 point)
        {
            var p = Vector4.Transform(new Vector4(local, 1), clip);
            if (p.W <= 1e-6f)
            {
                point = default;
                return false;
            }

            point = new Vector2(p.X / p.W * width * 0.5f, p.Y / p.W * height * 0.5f);
            return true;
        }
    }

    private VulkanWorldMesh MeshFor(WorldMesh mesh)
    {
        if (!_meshes.TryGetValue(mesh, out var gpu))
        {
            AllocationScope.Pause();
            gpu = VulkanWorldMesh.Create(_device, mesh);
            _meshes[mesh] = gpu;
            AllocationScope.Resume();
        }

        return gpu;
    }

    private void EnsureImage(WorldImage? image)
    {
        if (image is not null && !_images.ContainsKey(image))
        {
            AllocationScope.Pause();
            _images[image] = Upload(image);
            AllocationScope.Resume();
        }
    }

    private VulkanWorldImage ImageFor(WorldImage? image) =>
        image is not null && _images.TryGetValue(image, out var uploaded) ? uploaded : _white;

    private VulkanWorldImage Upload(WorldImage source)
    {
        var image = VulkanWorldImage.Create(_device, source.Width, source.Height, Format.R8G8B8A8Unorm, 0);
        var staging = VulkanWorldBuffer.Create(_device, (ulong)source.Rgba.Length, BufferUsageFlags.TransferSrcBit);
        try
        {
            fixed (byte* data = source.Rgba)
            {
                System.Buffer.MemoryCopy(data, staging.Mapped, (long)staging.Size, source.Rgba.Length);
            }

            _device.SubmitImmediate(commands =>
            {
                image.Barrier(_vk, commands, 0, 1, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
                    0, AccessFlags.TransferWriteBit, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit);
                var region = new BufferImageCopy
                {
                    ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                    ImageExtent = new Extent3D((uint)source.Width, (uint)source.Height, 1),
                };
                _vk.CmdCopyBufferToImage(commands, staging.Handle, image.Image, ImageLayout.TransferDstOptimal, 1, &region);
                image.RecordMips(_vk, commands, ImageLayout.TransferDstOptimal);
            });
        }
        finally
        {
            staging.Destroy(_device);
        }

        return image;
    }

    private VulkanWorldFrame AcquireFrame()
    {
        for (var i = 0; i < _frames.Length; i++)
        {
            var candidate = _frames[(_frameIndex + i) % _frames.Length];
            if (candidate.IsIdle)
            {
                _frameIndex = (_frameIndex + i + 1) % _frames.Length;
                candidate.Reset();
                return candidate;
            }
        }

        var oldest = _frames[_frameIndex];
        _device.WaitFor(oldest.Point);
        _frameIndex = (_frameIndex + 1) % _frames.Length;
        oldest.Reset();
        return oldest;
    }

    private void WaitForReaders(in DmabufAttributes attributes)
    {
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            _current!.AddWait(_semaphoreFd, RenderFences.ExportDmabufSyncFile(attributes.Fds[plane], forWrite: true));
        }
    }

    private void WaitForWriters(in DmabufAttributes attributes)
    {
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            _current!.AddWait(_semaphoreFd, RenderFences.ExportDmabufSyncFile(attributes.Fds[plane], forWrite: false));
        }
    }

    private VulkanWorldTarget TargetFor(IBuffer target)
    {
        if (_targets.TryGetValue(target, out var entry))
        {
            return entry;
        }

        AllocationScope.Pause();
        try
        {
            entry = VulkanWorldTarget.Create(_device, _pipelines, target);
            _targets[target] = entry;
            var handler = ForgetOnDestroy(target);
            _destroyHandlers[target] = handler;
            target.Destroyed += handler;
            return entry;
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private Action ForgetOnDestroy(IBuffer buffer) => () => ForgetTarget(buffer);

    private void ForgetTarget(IBuffer buffer)
    {
        _destroyHandlers.Remove(buffer);
        if (_targets.Remove(buffer, out var entry) && !_disposed)
        {
            _droppedTargets.Add((entry.LastPoint, entry));
        }
    }

    private void ReleaseDropped()
    {
        for (var i = _droppedTargets.Count - 1; i >= 0; i--)
        {
            if (_device.IsComplete(_droppedTargets[i].Point))
            {
                _droppedTargets[i].Target.Destroy(_device);
                _droppedTargets.RemoveAt(i);
            }
        }

        for (var i = _droppedImages.Count - 1; i >= 0; i--)
        {
            if (_device.IsComplete(_droppedImages[i].Point))
            {
                _droppedImages[i].Image.Destroy(_device);
                _droppedImages.RemoveAt(i);
            }
        }

        for (var i = _droppedMeshes.Count - 1; i >= 0; i--)
        {
            if (_device.IsComplete(_droppedMeshes[i].Point))
            {
                _droppedMeshes[i].Mesh.Destroy(_device);
                _droppedMeshes.RemoveAt(i);
            }
        }
    }

    private void EvictIdle(ulong point)
    {
        foreach (var state in _windows.Values)
        {
            foreach (var copy in state.Copies)
            {
                if (copy is not null)
                {
                    copy.LastPoint = point;
                }
            }
        }

        if (_frame % 120 != 0)
        {
            return;
        }

        foreach (var (mesh, gpu) in _meshes)
        {
            if (_frame - gpu.LastUsed > IdleFrames && !IsPrimitive(mesh))
            {
                _evict.Add(mesh);
            }
        }

        foreach (var mesh in _evict)
        {
            if (_meshes.Remove(mesh, out var gpu))
            {
                _droppedMeshes.Add((point, gpu));
            }
        }

        _evict.Clear();
    }

    private static bool IsPrimitive(WorldMesh mesh) =>
        ReferenceEquals(mesh, WorldPrimitives.Quad) || ReferenceEquals(mesh, WorldPrimitives.SlabEdges) ||
        ReferenceEquals(mesh, WorldPrimitives.SlabBack) || ReferenceEquals(mesh, WorldPrimitives.Flame);
}
