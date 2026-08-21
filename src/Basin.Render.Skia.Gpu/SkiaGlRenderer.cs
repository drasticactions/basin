using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.Render.Gl;
using SkiaSharp;

namespace Basin.Render.Skia;

public sealed class SkiaGlRenderer : IRenderer
{
    internal const uint GlTexture2D = 0x0DE1;
    internal const uint GlBgra8 = 0x93A1;
    internal const uint GlRgba8 = 0x8058;

    private readonly GlDevice _device;
    private readonly GRGlInterface _interface;
    private readonly GRContext _context;
    private readonly GRGlGetProcedureAddressDelegate _getProc;
    private readonly Dictionary<IBuffer, TargetEntry> _targets = [];
    private readonly SkiaGlRenderPass _pass;
    private readonly SKPaint _paint;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    public SkiaGlRenderer(string devicePath = "/dev/dri/renderD128")
    {
        _device = new GlDevice(devicePath);
        _getProc = GetProcAddress;
        _interface = GRGlInterface.CreateGles(_getProc)
            ?? throw new InvalidOperationException("Ganesh rejected the GLES interface on this context.");
        SkiaCensus.Track(_interface);
        var context = GRContext.CreateGl(_interface);
        if (context is null)
        {
            SkiaCensus.Release(_interface);
            _device.Dispose();
            throw new InvalidOperationException("Ganesh could not create a GL context.");
        }

        _context = SkiaCensus.Track(context);
        _paint = SkiaCensus.Track(new SKPaint());
        _pass = new SkiaGlRenderPass(this, _paint);
    }

    public GlDevice Device => _device;

    IRenderDevice? IRenderer.Device => Device;

    public static RenderStack CreateStack(string renderNodePath)
    {
        var renderer = new SkiaGlRenderer(renderNodePath);
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

    public GRContext Context => _context;

    public DrmFormatSet DmabufTextureFormats => _device.SampleableFormats;

    public ColorTransformCapability ColorTransform => ColorTransformCapability.Lut3D;

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
        _thread.Assert();
        if (buffer.TryGetDmabuf(out var attributes))
        {
            return DmabufTextureFormats.Contains(attributes.Format)
                ? SkiaGlDmabufTexture.TryImport(this, attributes)
                : null;
        }

        return new SkiaGlShmTexture(this, buffer);
    }

    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        return source.Sksl is null ? null : SkiaPixelShader.Create(source, uniforms);
    }

    public IColorLut? ImportLut(ColorLut3D lut)
    {
        _thread.Assert();
        return SkiaColorLut.Create(lut);
    }

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();

        if (_context.IsAbandoned)
        {
            DropTargets();
            throw new InvalidOperationException("The Ganesh context is abandoned; cached targets were dropped.");
        }

        if (!_targets.TryGetValue(target, out var entry))
        {
            entry = ImportTarget(target);
        }

        _device.WaitFence(options.WaitFenceFd);
        DirtyGlStateForDebug();

        _context.ResetContext(uint.MaxValue);
        _pass.Begin(target, entry, options.SignalFenceFd);
        return _pass;
    }

    public void NotifyGlStateTouched() => _context.ResetContext(uint.MaxValue);

    private TargetEntry ImportTarget(IBuffer target)
    {
        var native = GlRenderTarget.Create(_device, target);
        var format = native.IsCpuReadback ? GlRgba8 : GlBgra8;
        var colorType = native.IsCpuReadback ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        var backendTarget = SkiaCensus.Track(new GRBackendRenderTarget(
            target.Width, target.Height, sampleCount: 0, stencilBits: 0,
            new GRGlFramebufferInfo(native.Framebuffer, format)));

        var surface = SKSurface.Create(_context, backendTarget, GRSurfaceOrigin.TopLeft, colorType);
        if (surface is null)
        {
            SkiaCensus.Release(backendTarget);
            native.Dispose(_device);
            throw new InvalidOperationException("Ganesh rejected the render target's framebuffer format.");
        }

        var entry = new TargetEntry(native, backendTarget, SkiaCensus.Track(surface), surface.Canvas);
        _targets[target] = entry;
        target.Destroyed += () =>
        {
            if (_targets.Remove(target, out var dead))
            {
                ReleaseTarget(dead);
            }
        };
        return entry;
    }

    public void Dispose()
    {
        _thread.Assert();
        DropTargets();
        RenderFences.CloseFence(_completionFence);
        _completionFence = -1;
        SkiaCensus.Release(_paint);

        _context.AbandonContext(releaseResources: true);
        SkiaCensus.Release(_context);
        SkiaCensus.Release(_interface);
        _device.Dispose();
    }

    private void DropTargets()
    {
        foreach (var entry in _targets.Values)
        {
            ReleaseTarget(entry);
        }

        _targets.Clear();
    }

    private void ReleaseTarget(TargetEntry entry)
    {
        SkiaCensus.Release(entry.Surface);
        SkiaCensus.Release(entry.BackendTarget);
        entry.Native.Dispose(_device);
    }

    private static nint GetProcAddress(string name)
    {
        var native = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            unsafe
            {
                return (nint)Mesa.Native.Libegl.eglGetProcAddress((sbyte*)native);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(native);
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void DirtyGlStateForDebug()
    {
        var gl = _device.Gl;
        gl.BindFramebuffer(Silk.NET.OpenGLES.FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, 1, 1);
        gl.Enable(Silk.NET.OpenGLES.EnableCap.ScissorTest);
        gl.Scissor(0, 0, 1, 1);
        gl.BlendFunc(Silk.NET.OpenGLES.BlendingFactor.One, Silk.NET.OpenGLES.BlendingFactor.One);
        gl.UseProgram(0);
        gl.ActiveTexture(Silk.NET.OpenGLES.TextureUnit.Texture5);
    }

    internal sealed record TargetEntry(
        GlRenderTarget Native,
        GRBackendRenderTarget BackendTarget,
        SKSurface Surface,
        SKCanvas Canvas);
}
