using Silk.NET.OpenGLES;

namespace Basin.Render.Gl;

public sealed unsafe class GlRenderer : IRenderer
{
    private readonly GlDevice _device;
    private readonly Dictionary<IBuffer, GlRenderTarget> _targets = [];
    private readonly GlRenderPass _pass;

    internal readonly ShaderProgram TextureProgram;
    internal readonly ShaderProgram TextureLutProgram;
    internal readonly ShaderProgram SolidProgram;
    internal readonly ShaderProgram MeshProgram;
    internal readonly uint MeshVbo;
    internal readonly uint PassVao;

    public GlRenderer(string devicePath = "/dev/dri/renderD128")
    {
        _device = new GlDevice(devicePath);
        TextureProgram = new ShaderProgram(_device.Gl, GlShaders.Vertex, GlShaders.TextureFragment);
        TextureLutProgram = new ShaderProgram(_device.Gl, GlShaders.Vertex, GlShaders.TextureLutFragment);
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

    public DrmFormatSet DmabufTextureFormats => _device.SampleableFormats;

    public int DrmFd => _device.DrmFd;

    internal GL Gl => _device.Gl;

    public ColorTransformCapability ColorTransform => ColorTransformCapability.Lut3D;

    public bool SupportsBackdropEffects => true;

    public bool WaitsOnGpu => _device.WaitsOnGpu;

    public RenderFencePrecision FencePrecision => RenderFencePrecision.Context;

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
            return DmabufTextureFormats.Contains(attributes.Format)
                ? GlDmabufTexture.TryImport(_device, attributes)
                : null;
        }

        return new GlShmTexture(_device, buffer);
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

        return new GlColorLut(this, texture);
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
        if (!_targets.TryGetValue(target, out var entry))
        {
            entry = ImportTarget(target);
        }

        _device.FenceWait.Wait(options.WaitFenceFd);

        _pass.Begin(target, entry, options.SignalFenceFd);
        return _pass;
    }

    private GlRenderTarget ImportTarget(IBuffer target)
    {
        var entry = GlRenderTarget.Create(_device, target);
        _targets[target] = entry;
        target.Destroyed += () =>
        {
            if (_targets.Remove(target, out var stale))
            {
                stale.Dispose(_device);
            }
        };
        return entry;
    }

    public void Dispose()
    {
        foreach (var entry in _targets.Values)
        {
            entry.Dispose(_device);
        }

        _targets.Clear();
        RenderFences.CloseFence(_completionFence);
        _completionFence = -1;
        _pass.DisposeResources();
        TextureProgram.Dispose(_device.Gl);
        TextureLutProgram.Dispose(_device.Gl);
        SolidProgram.Dispose(_device.Gl);
        MeshProgram.Dispose(_device.Gl);
        _device.Gl.DeleteBuffer(MeshVbo);
        _device.Gl.DeleteVertexArray(PassVao);
        _device.Dispose();
    }
}
