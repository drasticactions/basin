using Basin.Capabilities;
using Basin.Color;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

public sealed unsafe class GlRenderer : IRenderer
{
    private readonly GlDevice _device;
    private readonly Dictionary<IBuffer, GlRenderTarget> _targets = [];
    private readonly HashSet<IBuffer> _watched = [];
    private readonly GlRenderPass _pass;

    internal readonly ShaderProgram TextureProgram;
    internal readonly ShaderProgram TextureLutProgram;
    internal readonly ShaderProgram TextureColorProgram;
    private readonly Dictionary<(ImageDescription Source, ImageDescription Output), GlColorTransform?> _colorTransforms =
        new(ImageDescriptionPairComparer.Instance);
    internal readonly ShaderProgram SolidProgram;
    internal readonly ShaderProgram MeshProgram;
    internal readonly uint MeshVbo;
    internal readonly uint PassVao;

    public GlRenderer(string devicePath = "/dev/dri/renderD128")
    {
        _device = new GlDevice(devicePath);
        TextureProgram = new ShaderProgram(_device.Gl, GlShaders.Vertex, GlShaders.TextureFragment);
        TextureLutProgram = new ShaderProgram(_device.Gl, GlShaders.Vertex, GlShaders.TextureLutFragment);
        TextureColorProgram = new ShaderProgram(_device.Gl, GlShaders.Vertex, GlShaders.TextureColorFragment);
        SolidProgram = new ShaderProgram(_device.Gl, GlShaders.Vertex, GlShaders.SolidFragment);
        MeshProgram = new ShaderProgram(_device.Gl, GlShaders.MeshVertex, GlShaders.MeshFragment);
        MeshVbo = _device.Gl.GenBuffer();
        PassVao = _device.Gl.GenVertexArray();
        _pass = new GlRenderPass(this, _device.Gl);
    }

    public GlDevice Device => _device;

    IRenderDevice? IRenderer.Device => Device;

    public static RenderStack CreateStack(string renderNodePath)
    {
        var renderer = new GlRenderer(renderNodePath);
        try
        {
            return new RenderStack(renderer, renderer.Device.CreateAllocator());
        }
        catch
        {
            renderer.Dispose();
            throw;
        }
    }

    public string DevicePath => _device.DevicePath;

    public DrmFormatSet DmabufTextureFormats => _textureFormats ??= TextureFormats();

    private DrmFormatSet? _textureFormats;

    private GlNvProgram? _nv;

    private DrmFormatSet TextureFormats()
    {
        if (!_device.ImportsLinearYuvPlanes)
        {
            return _device.SampleableFormats;
        }

        var formats = new DrmFormatSet();
        foreach (var format in _device.SampleableFormats.Formats)
        {
            foreach (var modifier in _device.SampleableFormats.ModifiersOf(format))
            {
                formats.Add(format, modifier);
            }
        }

        formats.Add(DrmFormat.Nv12, DrmFormatSet.ModifierLinear);
        return formats;
    }

    public IReadOnlyList<DrmFormat> ShmTextureFormats => ShmFormatLists.All;

    private GlYuvConverter? _yuv;

    public bool CanConvertYuv => true;

    public bool RendersHalfFloat => true;

    public bool ConvertToYuv(ITexture source, in FBox sourceBox, IBuffer target, YuvCoefficients coefficients, YuvRange range)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (!GlTextures.TryOpen(source, out var view) || view.External)
        {
            return false;
        }

        if (_yuv is null)
        {
            Basin.Diagnostics.AllocationScope.Pause();
            try
            {
                _yuv = new GlYuvConverter(_device);
            }
            finally
            {
                Basin.Diagnostics.AllocationScope.Resume();
            }
        }

        return _yuv.Convert(PassVao, view, sourceBox, target, coefficients, range);
    }

    public DrmFormatSet DmabufRenderFormats => _device.SampleableFormats;

    public int DrmFd => _device.DrmFd;

    internal GL Gl => _device.Gl;

    public ColorTransformCapability ColorTransform => ColorTransformCapability.Decomposed;

    internal GlColorTransform? TransformFor(ImageDescription? source, ImageDescription? output)
    {
        source ??= ImageDescription.SdrDefault;
        output ??= ImageDescription.SdrDefault;
        if (ReferenceEquals(source, output))
        {
            return null;
        }

        var key = (source, output);
        if (_colorTransforms.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Basin.Diagnostics.AllocationScope.Pause();
        try
        {
            var transform = source.IccData is null && !ColorLutBaker.IsIdentity(source, output)
                ? new GlColorTransform(ColorTransformParameters.From(source, output))
                : null;
            _colorTransforms[key] = transform;
            return transform;
        }
        finally
        {
            Basin.Diagnostics.AllocationScope.Resume();
        }
    }

    public bool SupportsBackdropEffects => true;

    public bool SupportsFrameFilters => true;

    public bool WaitsOnGpu => _device.WaitsOnGpu;

    public RenderFencePrecision FencePrecision => RenderFencePrecision.Context;

    public bool SupportsFilter(TextureFilter filter) => true;

    private int _completionFence = -1;

    internal void ReplaceCompletionFence(int fd)
    {
        RenderFences.CloseFence(_completionFence);
        _completionFence = fd;
    }

    public int ExportLastSubmissionFence()
    {
        var fd = _completionFence;
        _completionFence = -1;
        return fd;
    }

    public ITexture? ImportTexture(IBuffer buffer)
    {
        if (buffer.TryGetDmabuf(out var attributes))
        {
            if (attributes.Format == DrmFormat.Nv12)
            {
                return DmabufTextureFormats.Contains(DrmFormat.Nv12, attributes.Modifier)
                    ? GlYuvTexture.TryImport(_device, _nv ??= new GlNvProgram(_device.Gl, PassVao), attributes)
                    : null;
            }

            return DmabufTextureFormats.Contains(attributes.Format)
                ? GlDmabufTexture.TryImport(_device, attributes)
                : null;
        }

        return GlShmTexture.Uploads(buffer.Format) ? new GlShmTexture(_device, buffer) : null;
    }

    public IColorLut? ImportLut(ColorLut3D lut)
    {
        var gl = _device.Gl;
        var texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture3D, texture);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        fixed (float* data = lut.Data)
        {
            gl.TexImage3D(
                TextureTarget.Texture3D, 0, InternalFormat.Rgb16f,
                (uint)lut.Size, (uint)lut.Size, (uint)lut.Size, 0,
                PixelFormat.Rgb, PixelType.Float, data);
        }

        return new GlColorLut(this, texture, lut.ShaperNits);
    }

    internal void ReleaseLut(uint texture) => _device.Gl.DeleteTexture(texture);

    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        if (source.Glsl is null)
        {
            return null;
        }

        var fragment = GlPixelShader.BuildFragment(source.Glsl, uniforms, source.SamplesTexture);
        var program = new ShaderProgram(_device.Gl, GlShaders.Vertex, fragment);
        ShaderProgram? lutProgram = null;
        if (source.SamplesTexture)
        {
            var lutFragment = GlPixelShader.BuildFragment(source.Glsl, uniforms, source.SamplesTexture, lut: true);
            lutProgram = new ShaderProgram(_device.Gl, GlShaders.Vertex, lutFragment);
        }

        return new GlPixelShader(this, program, lutProgram, uniforms, source.SamplesTexture);
    }

    internal void ReleaseShader(ShaderProgram program) => program.Dispose(_device.Gl);

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        if (_targets.TryGetValue(target, out var entry) && entry.IsCpuReadback && entry.SeenWrites != target.WriteCount)
        {
            _targets.Remove(target);
            entry.Dispose(_device);
            entry = null;
        }

        entry ??= ImportTarget(target);

        _device.FenceWait.Wait(options.WaitFenceFd);

        _pass.Begin(target, entry, options.SignalFenceFd, options.ColorDescription);
        return _pass;
    }

    private GlRenderTarget ImportTarget(IBuffer target)
    {
        var entry = GlRenderTarget.Create(_device, target);
        entry.SeenWrites = target.WriteCount;
        _targets[target] = entry;
        if (_watched.Add(target))
        {
            target.Destroyed += () =>
            {
                _watched.Remove(target);
                if (_targets.Remove(target, out var stale))
                {
                    stale.Dispose(_device);
                }
            };
        }

        return entry;
    }

    public void Dispose()
    {
        _yuv?.Dispose();
        _yuv = null;
        _nv?.Dispose(_device.Gl);
        _nv = null;
        foreach (var entry in _targets.Values)
        {
            entry.Dispose(_device);
        }

        _targets.Clear();
        RenderFences.CloseFence(_completionFence);
        _completionFence = -1;
        TextureProgram.Dispose(_device.Gl);
        TextureLutProgram.Dispose(_device.Gl);
        TextureColorProgram.Dispose(_device.Gl);
        _colorTransforms.Clear();
        SolidProgram.Dispose(_device.Gl);
        MeshProgram.Dispose(_device.Gl);
        _device.Gl.DeleteBuffer(MeshVbo);
        _device.Gl.DeleteVertexArray(PassVao);
        _device.Dispose();
    }
}
