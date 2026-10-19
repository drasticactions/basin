using Basin.Capabilities;
using Basin.Diagnostics;
using Pixman;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

internal sealed unsafe class GlRenderPass : IRenderPass
{
    private readonly GlRenderer _renderer;
    private readonly GL _gl;
    private IBuffer? _target;
    private GlRenderTarget? _entry;

    private readonly List<GlDmabufTexture> _sampled = [];

    internal GlRenderPass(GlRenderer renderer, GL gl)
    {
        _renderer = renderer;
        _gl = gl;
    }

    private ImageDescription? _outputColor;

    internal void Begin(IBuffer target, GlRenderTarget entry, int signalFenceFd = -1, ImageDescription? outputColor = null)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _entry = entry;
        _signalFenceFd = signalFenceFd;
        _outputColor = outputColor;
        BindPassState();
    }

    private void BindPassState()
    {
        _gl.BindVertexArray(_renderer.PassVao);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _entry!.Framebuffer);
        _gl.Viewport(0, 0, (uint)_target!.Width, (uint)_target.Height);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
    }

    public void AddBackdropEffect(IBackdropEffect effect, in Box bounds, PixmanRegion32? clip = null, object? key = null) =>
        AddBackdropEffect(effect, bounds, bounds, clip, key);

    public void AddBackdropEffect(IBackdropEffect effect, in Box bounds, in Box extent, PixmanRegion32? clip, object? key) =>
        RecordBackdropEffect(effect, bounds, extent, clip, key, default);

    public void AddDeformedBackdropEffect(
        IBackdropEffect effect, in Box bounds, in Box extent, PixmanRegion32? clip, object? key, ReadOnlySpan<MeshVertex> mesh)
    {
        if (_backdropMesh.Length < mesh.Length)
        {
            _backdropMesh = new MeshVertex[Math.Max(mesh.Length, _backdropMesh.Length * 2)];
        }

        mesh.CopyTo(_backdropMesh);
        RecordBackdropEffect(effect, bounds, extent, clip, key, _backdropMesh.AsMemory(0, mesh.Length));
    }

    private MeshVertex[] _backdropMesh = [];

    private void RecordBackdropEffect(
        IBackdropEffect effect, in Box requested, in Box extent, PixmanRegion32? clip, object? key, ReadOnlyMemory<MeshVertex> mesh)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var bounds = requested.Intersect(new Box(0, 0, _target.Width, _target.Height));
        if (effect is not IGlBackdropEffect glEffect)
        {
            throw new ArgumentException("effect does not belong to this renderer");
        }

        if (bounds.IsEmpty)
        {
            return;
        }

        var backdrop = _entry!.BackdropTexture(_renderer.Device, _target.Width, _target.Height);

        var context = new GlBackdropContext
        {
            Device = _renderer.Device,
            Backdrop = backdrop,
            TargetWidth = _target.Width,
            TargetHeight = _target.Height,
            Bounds = bounds,
            Extent = extent,
            Mesh = mesh,
            Key = key,
        };
        var recorded = glEffect.Record(in context, out var result);
        BindPassState();
        if (!recorded || result.Texture == 0 || result.TextureWidth <= 0 || result.TextureHeight <= 0)
        {
            return;
        }

        var program = _renderer.TextureProgram;
        _gl.UseProgram(program.Program);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, result.Texture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.Uniform4(program.Dst, (float)bounds.X, bounds.Y, bounds.Width, bounds.Height);
        _gl.Uniform2(program.Target, (float)_target.Width, _target.Height);
        _gl.Uniform4(
            program.Src,
            (float)result.Source.X / result.TextureWidth,
            (float)result.Source.Y / result.TextureHeight,
            (float)result.Source.Width / result.TextureWidth,
            (float)result.Source.Height / result.TextureHeight);
        _gl.Uniform1(program.Alpha, 1f);
        _gl.Uniform1(program.ForceOpaque, 1f);
        SetTransform(program.Transform, RenderTransform.Identity);
        DrawClipped(clip);
    }

    public bool AddFrameFilter(IFrameFilter filter, ITexture source, in FrameFilterOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (filter is not IGlFilter glFilter)
        {
            throw new ArgumentException("filter does not belong to this renderer");
        }

        if (!glFilter.IsSupported)
        {
            return false;
        }

        uint sourceId;
        switch (source)
        {
            case GlDmabufTexture dmabuf:
                sourceId = dmabuf.TextureId;
                if (!dmabuf.SampledThisPass)
                {
                    dmabuf.SampledThisPass = true;
                    _sampled.Add(dmabuf);
                }

                break;
            case GlYuvTexture yuv:
                yuv.Acquire(out sourceId);
                break;
            case GlShmTexture shm:
                if (!shm.Acquire(out sourceId))
                {
                    return false;
                }

                break;
            default:
                throw new ArgumentException("texture does not belong to this renderer");
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, sourceId);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 0);

        var context = new GlFilterContext
        {
            Device = _renderer.Device,
            Source = sourceId,
            SourceWidth = source.Width,
            SourceHeight = source.Height,
            Target = _entry!.TargetTexture,
            TargetWidth = _target.Width,
            TargetHeight = _target.Height,
            Viewport = new Box(0, 0, _target.Width, _target.Height),
            Options = options,
        };
        var recorded = glFilter.Record(in context);
        BindPassState();
        return recorded;
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (box.IsEmpty)
        {
            return;
        }

        var r = color.R;
        var g = color.G;
        var b = color.B;
        if (_outputColor is not null && color.A > 0 && _renderer.TransformFor(null, _outputColor) is { } transform)
        {
            Span<double> rgb = stackalloc double[3];
            rgb[0] = Math.Clamp(r / color.A, 0, 1);
            rgb[1] = Math.Clamp(g / color.A, 0, 1);
            rgb[2] = Math.Clamp(b / color.A, 0, 1);
            transform.Parameters.Apply(rgb);
            r = (float)rgb[0] * color.A;
            g = (float)rgb[1] * color.A;
            b = (float)rgb[2] * color.A;
        }

        var program = _renderer.SolidProgram;
        _gl.UseProgram(program.Program);
        _gl.Uniform4(program.Dst, (float)box.X, box.Y, box.Width, box.Height);
        _gl.Uniform2(program.Target, (float)_target.Width, _target.Height);
        _gl.Uniform4(program.Color, r, g, b, color.A);
        SetTransform(program.Transform, RenderTransform.Identity);
        DrawClipped(clip);
    }

    public void Clear(in RenderColor color)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        _gl.Disable(EnableCap.Blend);
        AddRect(color, new Box(0, 0, _target.Width, _target.Height));
        _gl.Enable(EnableCap.Blend);
    }

    public void AddTexture(ITexture texture, in TextureRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (options.DstBox.IsEmpty || (!options.Transform.IsIdentity && !options.Transform.TryInvert(out _)))
        {
            return;
        }

        uint textureId;
        var forceOpaque = false;
        switch (texture)
        {
            case GlDmabufTexture dmabuf:
                textureId = dmabuf.TextureId;
                forceOpaque = !dmabuf.HasAlpha;
                if (!dmabuf.SampledThisPass)
                {
                    dmabuf.SampledThisPass = true;
                    _sampled.Add(dmabuf);
                }

                break;
            case GlYuvTexture yuv:
                yuv.Acquire(out textureId);
                forceOpaque = true;
                break;
            case GlShmTexture shm:
                if (!shm.Acquire(out textureId))
                {
                    return;
                }

                forceOpaque = !shm.HasAlpha;
                break;
            default:
                throw new ArgumentException("texture does not belong to this renderer");
        }

        var src = options.SrcBox.IsEmpty
            ? new FBox(0, 0, texture.Width, texture.Height)
            : options.SrcBox;

        var program = _renderer.TextureProgram;
        GlPixelShader? custom = null;
        switch (options.Shader)
        {
            case null:
                break;
            case GlPixelShader glShader:
                if (!glShader.SamplesTexture)
                {
                    throw new ArgumentException("shader does not sample a texture");
                }

                custom = glShader;
                program = glShader.Program;
                break;
            default:
                throw new ArgumentException("shader does not belong to this renderer");
        }

        if (options.Lut is GlColorLut lut)
        {
            program = custom is null ? _renderer.TextureLutProgram : custom.LutProgram!;
            _gl.UseProgram(program.Program);
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture3D, lut.TextureId);
            _gl.Uniform1(program.Lut, 1);
            if (program.Shaper >= 0)
            {
                _gl.Uniform1(program.Shaper, lut.ShaperNits);
            }
        }
        else if (custom is null && _renderer.TransformFor(options.ColorDescription, _outputColor) is { } colorTransform)
        {
            program = _renderer.TextureColorProgram;
            _gl.UseProgram(program.Program);
            _gl.Uniform3(program.M0, colorTransform.M0.X, colorTransform.M0.Y, colorTransform.M0.Z);
            _gl.Uniform3(program.M1, colorTransform.M1.X, colorTransform.M1.Y, colorTransform.M1.Z);
            _gl.Uniform3(program.M2, colorTransform.M2.X, colorTransform.M2.Y, colorTransform.M2.Z);
            _gl.Uniform4(program.Source, colorTransform.Source.X, colorTransform.Source.Y, colorTransform.Source.Z, colorTransform.Source.W);
            _gl.Uniform4(program.Output, colorTransform.Output.X, colorTransform.Output.Y, colorTransform.Output.Z, colorTransform.Output.W);
            _gl.Uniform4(program.Tone, colorTransform.Tone.X, colorTransform.Tone.Y, colorTransform.Tone.Z, colorTransform.Tone.W);
        }

        _gl.UseProgram(program.Program);
        if (custom is not null)
        {
            _gl.Uniform2(program.Size, (float)options.DstBox.Width, options.DstBox.Height);
            custom.WriteUniforms(_gl, lut: options.Lut is GlColorLut);
        }
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, textureId);
        var nearest = options.Filter == TextureFilter.Nearest;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, nearest ? (int)TextureMinFilter.Nearest : (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, nearest ? (int)TextureMagFilter.Nearest : (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.Uniform4(program.Dst, (float)options.DstBox.X, options.DstBox.Y, options.DstBox.Width, options.DstBox.Height);
        _gl.Uniform2(program.Target, (float)_target.Width, _target.Height);
        _gl.Uniform4(
            program.Src,
            (float)(src.X / texture.Width),
            (float)(src.Y / texture.Height),
            (float)(src.Width / texture.Width),
            (float)(src.Height / texture.Height));
        _gl.Uniform1(program.Alpha, options.Alpha);
        _gl.Uniform1(program.ForceOpaque, forceOpaque ? 1f : 0f);
        if (program.Pixel >= 0)
        {
            _gl.Uniform1(program.Pixel, options.Filter == TextureFilter.Pixel ? 1f : 0f);
        }

        SetTransform(program.Transform, options.Transform);
        var opaque = options.Opaque && options.Alpha >= 1f;
        if (opaque)
        {
            _gl.Disable(EnableCap.Blend);
        }

        DrawClipped(options.Clip);
        if (opaque)
        {
            _gl.Enable(EnableCap.Blend);
        }
    }

    public void AddShader(IPixelShader shader, in ShaderRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (shader is not GlPixelShader glShader)
        {
            throw new ArgumentException("shader does not belong to this renderer");
        }

        if (glShader.SamplesTexture)
        {
            throw new ArgumentException("shader samples a texture and must draw through AddTexture");
        }

        if (options.DstBox.IsEmpty)
        {
            return;
        }

        var program = glShader.Program;
        _gl.UseProgram(program.Program);
        _gl.Uniform4(program.Dst, (float)options.DstBox.X, options.DstBox.Y, options.DstBox.Width, options.DstBox.Height);
        _gl.Uniform2(program.Target, (float)_target.Width, _target.Height);
        _gl.Uniform2(program.Size, (float)options.DstBox.Width, options.DstBox.Height);
        _gl.Uniform1(program.Alpha, options.Alpha);
        SetTransform(program.Transform, RenderTransform.Identity);
        glShader.WriteUniforms(_gl);
        DrawClipped(options.Clip);
    }

    public void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (vertices.Length == 0)
        {
            return;
        }

        if (vertices.Length % 3 != 0)
        {
            throw new ArgumentException("vertices must be a whole number of triangles", nameof(vertices));
        }

        uint textureId = 0;
        var hasTexture = false;
        var forceOpaque = false;
        switch (texture)
        {
            case null:
                break;
            case GlDmabufTexture dmabuf:
                textureId = dmabuf.TextureId;
                hasTexture = true;
                forceOpaque = !dmabuf.HasAlpha;
                if (!dmabuf.SampledThisPass)
                {
                    dmabuf.SampledThisPass = true;
                    _sampled.Add(dmabuf);
                }

                break;
            case GlYuvTexture yuv:
                yuv.Acquire(out textureId);
                hasTexture = true;
                forceOpaque = true;
                break;
            case GlShmTexture shm:
                if (!shm.Acquire(out textureId))
                {
                    return;
                }

                hasTexture = true;
                forceOpaque = !shm.HasAlpha;
                break;
            default:
                throw new ArgumentException("texture does not belong to this renderer");
        }

        var program = _renderer.MeshProgram;
        _gl.UseProgram(program.Program);
        _gl.Uniform2(program.Target, (float)_target.Width, _target.Height);
        _gl.Uniform1(program.HasTexture, hasTexture ? 1f : 0f);
        _gl.Uniform1(program.ForceOpaque, forceOpaque ? 1f : 0f);
        if (hasTexture)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, textureId);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.Uniform4(program.Src, 0f, 0f, texture!.Width, texture.Height);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _renderer.MeshVbo);
        fixed (MeshVertex* data = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(MeshVertex)),
                data,
                BufferUsageARB.StreamDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.EnableVertexAttribArray(1);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)sizeof(MeshVertex), (void*)0);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)sizeof(MeshVertex), (void*)8);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, (uint)sizeof(MeshVertex), (void*)16);

        if (options.Blend == RenderBlend.Additive)
        {
            _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        }

        if (options.Clip is null)
        {
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertices.Length);
        }
        else
        {
            _gl.Enable(EnableCap.ScissorTest);
            foreach (var rect in RegionRects.Of(options.Clip))
            {
                _gl.Scissor(rect.X1, rect.Y1, (uint)(rect.X2 - rect.X1), (uint)(rect.Y2 - rect.Y1));
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertices.Length);
            }

            _gl.Disable(EnableCap.ScissorTest);
        }

        if (options.Blend == RenderBlend.Additive)
        {
            _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        }

        _gl.DisableVertexAttribArray(0);
        _gl.DisableVertexAttribArray(1);
        _gl.DisableVertexAttribArray(2);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    private int _scopedSubmits;

    public bool Submit()
    {
        if (_scopedSubmits < 30)
        {
            _scopedSubmits++;
            return SubmitCore();
        }

        AllocationScope.Begin(region: "GlSubmit", forgiving: true);
        try
        {
            return SubmitCore();
        }
        finally
        {
            AllocationScope.End();
        }
    }

    private bool SubmitCore()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var target = _target;
        var entry = _entry!;
        _target = null;
        _entry = null;

        if (entry.IsCpuReadback)
        {
            ClearSampled();
            entry.ReadInto(_renderer.Device, target);
            entry.SeenWrites = target.WriteCount;
        }
        else
        {
            PublishFence(entry);
        }

        if (_signalFenceFd >= 0)
        {
            _gl.Finish();
            RenderFences.SignalSyncobjFd(_renderer.DrmFd, _signalFenceFd);
            _signalFenceFd = -1;
        }

        return true;
    }

    private void PublishFence(GlRenderTarget entry)
    {
        var fence = _renderer.Device.ExportFence();
        if (fence < 0)
        {
            _gl.Flush();
            ClearSampled();
            return;
        }

        _renderer.ReplaceCompletionFence(RenderFences.DuplicateFence(fence));

        RenderFences.PublishFenceTo(entry.Attributes, forWrite: true, fence);
        foreach (var texture in _sampled)
        {
            texture.SampledThisPass = false;
            RenderFences.PublishFenceTo(texture.Attributes, forWrite: false, fence);
        }

        _sampled.Clear();
        RenderFences.CloseFence(fence);
    }

    private void ClearSampled()
    {
        foreach (var texture in _sampled)
        {
            texture.SampledThisPass = false;
        }

        _sampled.Clear();
    }

    private int _signalFenceFd = -1;

    private void SetTransform(int location, in RenderTransform transform)
    {
        Span<float> matrix = stackalloc float[9]
        {
            (float)transform.M11, (float)transform.M21, (float)transform.M31,
            (float)transform.M12, (float)transform.M22, (float)transform.M32,
            (float)transform.M13, (float)transform.M23, (float)transform.M33,
        };
        _gl.UniformMatrix3(location, 1, false, matrix);
    }

    private void DrawClipped(PixmanRegion32? clip)
    {
        if (clip is null)
        {
            _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            return;
        }

        _gl.Enable(EnableCap.ScissorTest);
        foreach (var rect in RegionRects.Of(clip))
        {
            _gl.Scissor(rect.X1, rect.Y1, (uint)(rect.X2 - rect.X1), (uint)(rect.Y2 - rect.Y1));
            _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        }

        _gl.Disable(EnableCap.ScissorTest);
    }
}
