using Basin.Capabilities;
using Basin.Render.Vulkan;
using Hexa.NET.ImGui;
using Pixman;
using Silk.NET.Vulkan;

namespace Basin.UI.ImGui;

internal sealed unsafe class ImGuiVulkanTarget : IImGuiTarget
{
    private const int MaxFrames = 3;
    private const int MaxSpares = 2;
    private const nint ResetRenderState = -8;

    private sealed class Wrap
    {
        public required VulkanDeviceImage Image;
        public required Framebuffer Framebuffer;
        public ulong LastUsed;
    }

    private readonly ImGuiVulkanBackend _backend;
    private readonly VulkanDevice _device;
    private readonly List<BufferBase> _retired = [];
    private readonly List<BufferBase> _spares = [];
    private readonly HashSet<BufferBase> _watched = [];
    private readonly Dictionary<BufferBase, Wrap> _wraps = [];
    private readonly List<Wrap> _dropped = [];
    private readonly List<ImGuiVulkanFrame> _frames = [];
    private BufferBase? _target;
    private BufferBase? _front;
    private ulong _lastPoint;
    private int _nextFrame;
    private int _width;
    private int _height;
    private bool _produced;
    private bool _disposed;

    internal ImGuiVulkanTarget(ImGuiVulkanBackend backend)
    {
        _backend = backend;
        _device = backend.Device;
    }

    internal int FrameSlots => _frames.Count;

    public bool Configure(int width, int height)
    {
        if (_disposed || width <= 0 || height <= 0)
        {
            return false;
        }

        if (width == _width && height == _height)
        {
            return true;
        }

        DropSpares();
        if (_target is not null && !ReferenceEquals(_target, _front))
        {
            Retire(_target);
        }

        _target = null;
        (_width, _height) = (width, height);
        return Back() is not null;
    }

    public void Render(ImDrawData* data)
    {
        if (_disposed)
        {
            return;
        }

        var target = Back() ?? throw new InvalidOperationException("the imgui chrome could not allocate a buffer.");
        var wrap = WrapOf(target);
        var frame = AcquireFrame();
        WaitForReaders(target);
        var commands = _device.BeginCommands();
        wrap.Image.RecordForeignAcquire(commands);
        _backend.Textures.Record(commands);
        Record(commands, frame, wrap.Framebuffer, data, target.Width, target.Height);

        ulong point;
        bool fenced;
        if (target.TryGetDmabuf(out var attributes))
        {
            fenced = _device.PublishWriteFence(commands, attributes, wrap.Image, out point);
        }
        else
        {
            wrap.Image.RecordForeignRelease(commands);
            point = _device.Submit(commands);
            fenced = false;
        }

        if (!fenced)
        {
            _device.WaitFor(point);
        }

        frame.Point = point;
        wrap.LastUsed = point;
        _lastPoint = point;
        _backend.Textures.MarkSubmitted(point);
        _backend.ReportSync(fenced);
        ReleaseDropped();
        _produced = true;
    }

    public bool TryAcquire(PixmanRegion32? damage, out UIFrame frame)
    {
        if (_disposed || !_produced || _target is null)
        {
            frame = default;
            return false;
        }

        if (_front is not null && !ReferenceEquals(_front, _target))
        {
            Retire(_front);
        }

        _front = _target;
        frame = new UIFrame(_front.Lock(), damage);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _backend.Forget(this);
        if (_lastPoint > 0)
        {
            _device.WaitFor(_lastPoint);
        }

        foreach (var frame in _frames)
        {
            frame.Dispose();
        }

        _frames.Clear();
        foreach (var (_, wrap) in _wraps)
        {
            Destroy(wrap);
        }

        _wraps.Clear();
        foreach (var wrap in _dropped)
        {
            Destroy(wrap);
        }

        _dropped.Clear();
        if (_target is not null && !ReferenceEquals(_target, _front) && !_target.IsDestroyed)
        {
            DestroyBuffer(_target);
        }

        if (_front is not null && !_front.IsDestroyed)
        {
            DestroyBuffer(_front);
        }

        foreach (var buffer in _retired.ToArray())
        {
            if (!buffer.IsDestroyed)
            {
                DestroyBuffer(buffer);
            }
        }

        _retired.Clear();
        foreach (var spare in _spares)
        {
            if (!spare.IsDestroyed)
            {
                DestroyBuffer(spare);
            }
        }

        _spares.Clear();
        _watched.Clear();
        _target = null;
        _front = null;
    }

    private void Record(CommandBuffer commands, ImGuiVulkanFrame frame, Framebuffer framebuffer, ImDrawData* data, int width, int height)
    {
        var vk = _device.Api;
        var vertexBytes = data->TotalVtxCount * sizeof(ImDrawVert);
        var indexBytes = data->TotalIdxCount * sizeof(ushort);
        ImGuiVulkanBuffer? vertices = null;
        ImGuiVulkanBuffer? indices = null;
        if (vertexBytes > 0 && indexBytes > 0)
        {
            vertices = frame.Vertices(vertexBytes);
            indices = frame.Indices(indexBytes);
            var vertexAt = vertices.Mapped;
            var indexAt = indices.Mapped;
            var lists = data->CmdLists;
            for (var l = 0; l < lists.Size; l++)
            {
                var list = lists.Data[l].Handle;
                if (list is null)
                {
                    continue;
                }

                var listVertices = list->VtxBuffer.Size * sizeof(ImDrawVert);
                var listIndices = list->IdxBuffer.Size * sizeof(ushort);
                System.Buffer.MemoryCopy(list->VtxBuffer.Data, vertexAt, listVertices, listVertices);
                System.Buffer.MemoryCopy(list->IdxBuffer.Data, indexAt, listIndices, listIndices);
                vertexAt += listVertices;
                indexAt += listIndices;
            }
        }

        var clear = default(ClearValue);
        var passBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _backend.Pipeline.Pass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)width, (uint)height)),
            ClearValueCount = 1,
            PClearValues = &clear,
        };
        vk.CmdBeginRenderPass(commands, in passBegin, SubpassContents.Inline);
        if (vertices is not null && indices is not null)
        {
            SetState(commands, data, vertices, indices, width, height);
            Draw(commands, data, vertices, indices, width, height);
        }

        vk.CmdEndRenderPass(commands);
    }

    private void Draw(
        CommandBuffer commands, ImDrawData* data, ImGuiVulkanBuffer vertices, ImGuiVulkanBuffer indices, int width, int height)
    {
        var vk = _device.Api;
        var displayX = data->DisplayPos.X;
        var displayY = data->DisplayPos.Y;
        var scaleX = data->FramebufferScale.X;
        var scaleY = data->FramebufferScale.Y;
        var globalVertex = 0;
        var globalIndex = 0;
        var lists = data->CmdLists;
        for (var l = 0; l < lists.Size; l++)
        {
            var list = lists.Data[l].Handle;
            if (list is null)
            {
                continue;
            }

            for (var c = 0; c < list->CmdBuffer.Size; c++)
            {
                ref var command = ref list->CmdBuffer.Data[c];
                if (command.UserCallback is not null)
                {
                    if ((nint)command.UserCallback == ResetRenderState)
                    {
                        SetState(commands, data, vertices, indices, width, height);
                    }

                    continue;
                }

                var x1 = Math.Max(0f, (command.ClipRect.X - displayX) * scaleX);
                var y1 = Math.Max(0f, (command.ClipRect.Y - displayY) * scaleY);
                var x2 = Math.Min(width, (command.ClipRect.Z - displayX) * scaleX);
                var y2 = Math.Min(height, (command.ClipRect.W - displayY) * scaleY);
                if (x2 <= x1 || y2 <= y1 || command.ElemCount == 0
                    || _backend.Textures.Of(ImGuiTextures.Resolve(command.TexRef)) is not { } texture)
                {
                    continue;
                }

                var scissor = new Rect2D(
                    new Offset2D((int)x1, (int)y1), new Extent2D((uint)(x2 - (int)x1), (uint)(y2 - (int)y1)));
                vk.CmdSetScissor(commands, 0, 1, in scissor);
                var set = texture.Set;
                vk.CmdBindDescriptorSets(
                    commands, PipelineBindPoint.Graphics, _backend.Pipeline.Layout, 0, 1, &set, 0, null);
                vk.CmdDrawIndexed(
                    commands, command.ElemCount, 1, (uint)(command.IdxOffset + globalIndex),
                    (int)(command.VtxOffset + globalVertex), 0);
            }

            globalVertex += list->VtxBuffer.Size;
            globalIndex += list->IdxBuffer.Size;
        }
    }

    private void SetState(
        CommandBuffer commands, ImDrawData* data, ImGuiVulkanBuffer vertices, ImGuiVulkanBuffer indices, int width, int height)
    {
        var vk = _device.Api;
        vk.CmdBindPipeline(commands, PipelineBindPoint.Graphics, _backend.Pipeline.Pipeline);
        var vertexBuffer = vertices.Handle;
        ulong vertexOffset = 0;
        vk.CmdBindVertexBuffers(commands, 0, 1, &vertexBuffer, &vertexOffset);
        vk.CmdBindIndexBuffer(commands, indices.Handle, 0, IndexType.Uint16);
        var viewport = new Viewport(0, 0, width, height, 0, 1);
        vk.CmdSetViewport(commands, 0, 1, in viewport);

        var push = stackalloc float[4];
        push[0] = 2f / data->DisplaySize.X;
        push[1] = 2f / data->DisplaySize.Y;
        push[2] = -1f - (data->DisplayPos.X * push[0]);
        push[3] = -1f - (data->DisplayPos.Y * push[1]);
        vk.CmdPushConstants(
            commands, _backend.Pipeline.Layout, ShaderStageFlags.VertexBit, 0, ImGuiVulkanPipeline.PushConstantSize, push);
    }

    private BufferBase? Back()
    {
        if (_target is not null && !ReferenceEquals(_target, _front))
        {
            return _target;
        }

        var allocated = TakeSpare() ?? _backend.Allocator.Allocate(
            _width, _height, DrmFormat.Argb8888, _backend.Modifiers, BufferUse.Render | BufferUse.Scanout);
        if (allocated is null)
        {
            return null;
        }

        _target = allocated;
        return allocated;
    }

    private ImGuiVulkanFrame AcquireFrame()
    {
        if (_frames.Count < MaxFrames)
        {
            var fresh = new ImGuiVulkanFrame(_device);
            _frames.Add(fresh);
            return fresh;
        }

        var frame = _frames[_nextFrame];
        _nextFrame = (_nextFrame + 1) % MaxFrames;
        if (!frame.IsIdle)
        {
            _device.WaitFor(frame.Point);
        }

        return frame;
    }

    private void WaitForReaders(BufferBase target)
    {
        if (!target.TryGetDmabuf(out var attributes))
        {
            return;
        }

        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            var fence = RenderFences.ExportDmabufSyncFile(attributes.Fds[plane], forWrite: true);
            if (fence < 0)
            {
                continue;
            }

            _device.FenceWait.Wait(fence);
            RenderFences.CloseFence(fence);
        }
    }

    private Wrap WrapOf(BufferBase buffer)
    {
        if (_wraps.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        if (!buffer.TryGetDmabuf(out var attributes))
        {
            throw new InvalidOperationException("an ImGui Vulkan surface draws only into a dmabuf.");
        }

        var image = VulkanDeviceImage.TryImport(_device, attributes, ImageUsageFlags.ColorAttachmentBit)
            ?? throw new InvalidOperationException(
                $"{_device.DevicePath} would not import a {buffer.Width}x{buffer.Height} chrome buffer as a color attachment.");
        Framebuffer framebuffer;
        try
        {
            framebuffer = _backend.Pipeline.CreateFramebuffer(image.View, image.Width, image.Height);
        }
        catch
        {
            image.Dispose();
            throw;
        }

        var wrap = new Wrap { Image = image, Framebuffer = framebuffer };
        _wraps[buffer] = wrap;
        return wrap;
    }

    private void Retire(BufferBase buffer)
    {
        if (Reusable(buffer) && buffer.LockCount == 0)
        {
            _spares.Add(buffer);
            return;
        }

        if (buffer.LockCount == 0)
        {
            Discard(buffer);
            return;
        }

        _retired.Add(buffer);
        if (_watched.Add(buffer))
        {
            Watch(buffer);
        }
    }

    private void Watch(BufferBase buffer) => buffer.Released += () => OnReleased(buffer);

    private void OnReleased(BufferBase buffer)
    {
        if (!_retired.Remove(buffer) || buffer.IsDestroyed)
        {
            return;
        }

        if (!_disposed && Reusable(buffer))
        {
            _spares.Add(buffer);
            return;
        }

        Discard(buffer);
    }

    private bool Reusable(BufferBase buffer) =>
        !_disposed && _spares.Count < MaxSpares && buffer.Width == _width && buffer.Height == _height
        && !ReferenceEquals(buffer, _target);

    private BufferBase? TakeSpare()
    {
        for (var i = 0; i < _spares.Count; i++)
        {
            var spare = _spares[i];
            if (spare.IsDestroyed)
            {
                _spares.RemoveAt(i--);
                continue;
            }

            if (spare.LockCount == 0)
            {
                _spares.RemoveAt(i);
                return spare;
            }
        }

        return null;
    }

    private void DropSpares()
    {
        foreach (var spare in _spares)
        {
            Discard(spare);
        }

        _spares.Clear();
    }

    private void Discard(BufferBase buffer)
    {
        _watched.Remove(buffer);
        if (_wraps.Remove(buffer, out var wrap))
        {
            if (wrap.LastUsed == 0 || _device.IsComplete(wrap.LastUsed))
            {
                Destroy(wrap);
            }
            else
            {
                _dropped.Add(wrap);
            }
        }

        if (!buffer.IsDestroyed)
        {
            DestroyBuffer(buffer);
        }
    }

    private void ReleaseDropped()
    {
        for (var i = _dropped.Count - 1; i >= 0; i--)
        {
            if (_device.IsComplete(_dropped[i].LastUsed))
            {
                Destroy(_dropped[i]);
                _dropped.RemoveAt(i);
            }
        }
    }

    private void Destroy(Wrap wrap)
    {
        _device.Api.DestroyFramebuffer(_device.Device, wrap.Framebuffer, null);
        wrap.Image.Dispose();
    }

    private static void DestroyBuffer(BufferBase buffer) => buffer.Destroy();
}
