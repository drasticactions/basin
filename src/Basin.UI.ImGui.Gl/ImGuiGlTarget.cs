using Basin.Capabilities;
using Basin.Render.Gl;
using Hexa.NET.ImGui;
using Pixman;
using Silk.NET.OpenGLES;

namespace Basin.UI.ImGui;

internal sealed unsafe class ImGuiGlTarget : IImGuiTarget
{
    private const int MaxSpares = 2;
    private const nint ResetRenderState = -8;

    private readonly ImGuiGlBackend _backend;
    private readonly List<IBuffer> _retired = [];
    private readonly List<IBuffer> _spares = [];
    private readonly HashSet<IBuffer> _watched = [];
    private readonly Dictionary<IBuffer, GlRenderTarget> _wraps = [];
    private IBuffer? _target;
    private IBuffer? _front;
    private int _width;
    private int _height;
    private bool _produced;
    private bool _disposed;

    internal ImGuiGlTarget(ImGuiGlBackend backend)
    {
        _backend = backend;
    }

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

        using var current = _backend.Context.Enter();
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

        using var current = _backend.Context.Enter();
        var target = Back() ?? throw new InvalidOperationException("the imgui chrome could not allocate a buffer.");
        var native = WrapOf(target);
        WaitForReaders(target);

        var gl = _backend.Device.Gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, native.Framebuffer);
        gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
        gl.Disable(EnableCap.ScissorTest);
        gl.ColorMask(true, true, true, true);
        gl.ClearColor(0f, 0f, 0f, 0f);
        gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        Draw(gl, data, target.Width, target.Height);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        PublishWriteFence(target);
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
            using var current = _backend.Context.Enter();
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
        using var current = _backend.Context.Enter();
        foreach (var (_, native) in _wraps)
        {
            native.Dispose(_backend.Device);
        }

        _wraps.Clear();
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

    private void Draw(GL gl, ImDrawData* data, int width, int height)
    {
        var program = _backend.Program;
        var displayX = data->DisplayPos.X;
        var displayY = data->DisplayPos.Y;
        var scaleX = data->FramebufferScale.X;
        var scaleY = data->FramebufferScale.Y;
        SetState(gl, program, data);

        var lists = data->CmdLists;
        for (var l = 0; l < lists.Size; l++)
        {
            var list = lists.Data[l].Handle;
            if (list is null)
            {
                continue;
            }

            program.Upload(
                list->VtxBuffer.Data, list->VtxBuffer.Size * sizeof(ImDrawVert),
                list->IdxBuffer.Data, list->IdxBuffer.Size * sizeof(ushort));
            for (var c = 0; c < list->CmdBuffer.Size; c++)
            {
                ref var command = ref list->CmdBuffer.Data[c];
                if (command.UserCallback is not null)
                {
                    if ((nint)command.UserCallback == ResetRenderState)
                    {
                        SetState(gl, program, data);
                    }

                    continue;
                }

                var x1 = (command.ClipRect.X - displayX) * scaleX;
                var y1 = (command.ClipRect.Y - displayY) * scaleY;
                var x2 = (command.ClipRect.Z - displayX) * scaleX;
                var y2 = (command.ClipRect.W - displayY) * scaleY;
                x1 = Math.Max(0, x1);
                y1 = Math.Max(0, y1);
                x2 = Math.Min(width, x2);
                y2 = Math.Min(height, y2);
                if (x2 <= x1 || y2 <= y1 || command.ElemCount == 0)
                {
                    continue;
                }

                gl.Scissor((int)x1, (int)y1, (uint)(x2 - (int)x1), (uint)(y2 - (int)y1));
                gl.BindTexture(TextureTarget.Texture2D, _backend.NameOf(ImGuiTextures.Resolve(command.TexRef)));
                var offset = (void*)(nint)(command.IdxOffset * sizeof(ushort));
                if (command.VtxOffset != 0 && program.HasBaseVertex)
                {
                    gl.DrawElementsBaseVertex(
                        PrimitiveType.Triangles, command.ElemCount, DrawElementsType.UnsignedShort, offset,
                        (int)command.VtxOffset);
                }
                else
                {
                    gl.DrawElements(PrimitiveType.Triangles, command.ElemCount, DrawElementsType.UnsignedShort, offset);
                }
            }
        }

        gl.Disable(EnableCap.ScissorTest);
        gl.BindVertexArray(0);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private static void SetState(GL gl, ImGuiGlProgram program, ImDrawData* data)
    {
        gl.Enable(EnableCap.Blend);
        gl.BlendEquation(GLEnum.FuncAdd);
        gl.BlendFuncSeparate(
            BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.StencilTest);
        gl.Enable(EnableCap.ScissorTest);
        gl.UseProgram(program.Program);
        gl.BindVertexArray(program.Vao);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.Uniform1(program.Sampler, 0);

        var left = data->DisplayPos.X;
        var right = left + data->DisplaySize.X;
        var top = data->DisplayPos.Y;
        var bottom = top + data->DisplaySize.Y;
        Span<float> projection = stackalloc float[16];
        projection.Clear();
        projection[0] = 2f / (right - left);
        projection[5] = 2f / (bottom - top);
        projection[10] = -1f;
        projection[12] = (right + left) / (left - right);
        projection[13] = (top + bottom) / (top - bottom);
        projection[15] = 1f;
        gl.UniformMatrix4(program.Projection, 1, false, projection);
    }

    private IBuffer? Back()
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

    private void PublishWriteFence(IBuffer target)
    {
        var device = _backend.Device;
        var fence = target.TryGetDmabuf(out var attributes) ? device.ExportFence() : -1;
        if (fence < 0)
        {
            device.Gl.Finish();
            _backend.ReportSync(fenced: false);
            return;
        }

        RenderFences.PublishFenceTo(attributes, forWrite: true, fence);
        RenderFences.CloseFence(fence);
        _backend.ReportSync(fenced: true);
    }

    private void WaitForReaders(IBuffer target)
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

            _backend.Device.WaitFence(fence);
            RenderFences.CloseFence(fence);
        }
    }

    private GlRenderTarget WrapOf(IBuffer buffer)
    {
        if (_wraps.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        var native = GlRenderTarget.Create(_backend.Device, buffer);
        _wraps[buffer] = native;
        return native;
    }

    private void Retire(IBuffer buffer)
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

    private void Watch(IBuffer buffer) => buffer.Released += () => OnReleased(buffer);

    private void OnReleased(IBuffer buffer)
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

        using var current = _backend.Context.Enter();
        Discard(buffer);
    }

    private bool Reusable(IBuffer buffer) =>
        !_disposed && _spares.Count < MaxSpares && buffer.Width == _width && buffer.Height == _height
        && !ReferenceEquals(buffer, _target);

    private IBuffer? TakeSpare()
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

    private void Discard(IBuffer buffer)
    {
        _watched.Remove(buffer);
        if (_wraps.Remove(buffer, out var native))
        {
            native.Dispose(_backend.Device);
        }

        if (!buffer.IsDestroyed)
        {
            DestroyBuffer(buffer);
        }
    }

    private static void DestroyBuffer(IBuffer buffer)
    {
        if (buffer is BufferBase concrete)
        {
            concrete.Destroy();
        }
    }
}
