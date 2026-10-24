using Basin.Capabilities;
using Basin.Color;
using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

/// <summary>
/// Implements <see cref="IRenderer"/> on GLES 3 through EGL on one DRM node. It takes decomposed color transforms and draws on the thread that created it.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="GlRenderer"/> class. It throws <see cref="InvalidOperationException"/> when the node cannot be opened.
    /// </summary>
    /// <param name="devicePath">The path of the DRM node to render on, for example a render node.</param>
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

    /// <summary>
    /// Gets the GL device that the renderer owns and disposes.
    /// </summary>
    public GlDevice Device => _device;

    IRenderDevice? IRenderer.Device => Device;

    /// <summary>
    /// Creates a renderer and a GBM allocator on one render node.
    /// </summary>
    /// <param name="renderNodePath">The path of the DRM render node.</param>
    /// <returns>The renderer and the allocator, which the caller disposes. On failure the renderer is disposed and the exception propagates.</returns>
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

    /// <summary>
    /// Gets the path of the DRM node that the renderer opened.
    /// </summary>
    public string DevicePath => _device.DevicePath;

    /// <summary>
    /// Gets the dmabuf formats and modifiers that <see cref="ImportTexture"/> can sample. Linear NV12 is added when the device imports linear YUV planes.
    /// </summary>
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

    /// <inheritdoc/>
    public IReadOnlyList<DrmFormat> ShmTextureFormats => ShmFormatLists.All;

    private GlYuvConverter? _yuv;

    /// <inheritdoc/>
    public bool CanConvertYuv => true;

    /// <inheritdoc/>
    public bool RendersHalfFloat => true;

    /// <inheritdoc/>
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

    /// <summary>
    /// Gets the dmabuf formats and modifiers that a pass can draw into: ARGB8888 and XRGB8888 with each modifier that EGL imports as a 2D texture, plus the implicit modifier.
    /// </summary>
    public DrmFormatSet DmabufRenderFormats => _device.SampleableFormats;

    /// <summary>
    /// Gets the fd of the DRM node. The device owns it.
    /// </summary>
    public int DrmFd => _device.DrmFd;

    internal GL Gl => _device.Gl;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool SupportsBackdropEffects => true;

    /// <inheritdoc/>
    public bool SupportsFrameFilters => true;

    /// <inheritdoc/>
    public bool WaitsOnGpu => _device.WaitsOnGpu;

    /// <inheritdoc/>
    public RenderFencePrecision FencePrecision => RenderFencePrecision.Context;

    /// <summary>
    /// Tells whether the renderer draws a texture filter as specified. The GL renderer draws every filter itself.
    /// </summary>
    /// <param name="filter">The filter to check.</param>
    /// <returns><see langword="true"/> for every filter.</returns>
    public bool SupportsFilter(TextureFilter filter) => true;

    private int _completionFence = -1;

    internal void ReplaceCompletionFence(int fd)
    {
        RenderFences.CloseFence(_completionFence);
        _completionFence = fd;
    }

    /// <inheritdoc/>
    public int ExportLastSubmissionFence()
    {
        var fd = _completionFence;
        _completionFence = -1;
        return fd;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <summary>
    /// Deletes every program, target and cached object, and disposes the device.
    /// </summary>
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
