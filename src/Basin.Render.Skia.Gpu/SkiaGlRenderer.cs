using Basin.Render.Skia;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Basin.Render.Gl;
using SkiaSharp;

namespace Basin.Render.Skia;

/// <summary>
/// The Skia Ganesh renderer on a GLES context. It draws on a DRM render node and runs on the
/// compositor thread only.
/// </summary>
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
    private readonly HashSet<IBuffer> _watched = [];
    private readonly SkiaGlRenderPass _pass;
    private readonly SKPaint _paint;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Dictionary<(uint Texture, int Width, int Height), (GRBackendTexture Backend, SKImage Image)> _backdropImages = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaGlRenderer"/> class. It opens its own GL device
    /// on the render node.
    /// </summary>
    /// <param name="devicePath">The DRM render node to open.</param>
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
        _device.TextureDeleting += ForgetBackdropImage;
    }

    private void ForgetBackdropImage(uint texture)
    {
        while (true)
        {
            var found = false;
            (uint Texture, int Width, int Height) stale = default;
            foreach (var key in _backdropImages.Keys)
            {
                if (key.Texture == texture)
                {
                    stale = key;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return;
            }

            _backdropImages.Remove(stale, out var cached);
            SkiaCensus.Release(cached.Image);
            SkiaCensus.Release(cached.Backend);
        }
    }

    /// <summary>
    /// Gets the GL device that the renderer draws on. The renderer owns it.
    /// </summary>
    public GlDevice Device => _device;

    IRenderDevice? IRenderer.Device => Device;

    /// <summary>
    /// Creates a render stack with a new <see cref="SkiaGlRenderer"/> and an allocator on the same
    /// device. The renderer is disposed when the allocator cannot be made.
    /// </summary>
    /// <param name="renderNodePath">The DRM render node to open, for example <c>/dev/dri/renderD128</c>.</param>
    /// <returns>The stack, which owns the renderer and the allocator.</returns>
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

    /// <summary>
    /// Gets the Ganesh context. The renderer owns it.
    /// </summary>
    public GRContext Context => _context;

    /// <summary>
    /// Gets a value indicating whether a pass can run backdrop effects. It is always <see
    /// langword="true"/> on this renderer.
    /// </summary>
    public bool SupportsBackdropEffects => true;

    internal bool BackdropImage(uint texture, int width, int height, out SKImage image)
    {
        if (_backdropImages.TryGetValue((texture, width, height), out var cached))
        {
            image = cached.Image;
            return true;
        }

        var backend = SkiaCensus.Track(new GRBackendTexture(
            width, height, mipmapped: false, new GRGlTextureInfo(GlTexture2D, texture, GlRgba8)));
        var wrapped = SKImage.FromTexture(_context, backend, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (wrapped is null)
        {
            SkiaCensus.Release(backend);
            image = null!;
            return false;
        }

        image = SkiaCensus.Track(wrapped);
        _backdropImages[(texture, width, height)] = (backend, image);
        return true;
    }

    /// <inheritdoc/>
    public DrmFormatSet DmabufTextureFormats => _device.SampleableFormats;

    /// <inheritdoc/>
    public DrmFormatSet DmabufRenderFormats => _device.SampleableFormats;

    /// <summary>
    /// Gets how the renderer takes a color conversion. It is always <see
    /// cref="ColorTransformCapability.Decomposed"/>.
    /// </summary>
    public ColorTransformCapability ColorTransform => ColorTransformCapability.Decomposed;

    internal SkiaColorTransforms ColorTransforms { get; } = new();

    /// <inheritdoc/>
    public bool WaitsOnGpu => _device.WaitsOnGpu;

    /// <summary>
    /// Gets how much a signal fence of a pass promises. It is always <see
    /// cref="RenderFencePrecision.Context"/>.
    /// </summary>
    public RenderFencePrecision FencePrecision => RenderFencePrecision.Context;

    private int _completionFence = -1;

    internal void ReplaceCompletionFence(int fd)
    {
        RenderFences.CloseFence(_completionFence);
        _completionFence = fd;
    }

    /// <summary>
    /// Takes the completion fence of the most recently submitted pass. A second call returns -1 until
    /// another pass submits.
    /// </summary>
    /// <returns>A sync_file fd that the caller owns, or -1 when there is no fence.</returns>
    public int ExportLastSubmissionFence()
    {
        var fd = _completionFence;
        _completionFence = -1;
        return fd;
    }

    /// <summary>
    /// Imports a dmabuf as a GPU texture, or wraps a memory buffer that uploads on each change. It
    /// never throws for a foreign or hostile buffer.
    /// </summary>
    /// <param name="buffer">The buffer to import.</param>
    /// <returns>The texture, which the caller disposes, or <see langword="null"/> when the dmabuf format is not in <see cref="DmabufTextureFormats"/> or the import fails.</returns>
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

    /// <summary>
    /// Compiles the SkSL variant of a pixel shader. A source that does not compile throws with the
    /// compiler log.
    /// </summary>
    /// <param name="source">The shader source. Only its SkSL text is read.</param>
    /// <param name="uniforms">The uniforms that the shader declares after the uniforms of basin, in order.</param>
    /// <returns>The shader, which the caller disposes, or <see langword="null"/> when the source has no SkSL.</returns>
    public IPixelShader? CompilePixelShader(in PixelShaderSource source, ReadOnlySpan<PixelShaderUniform> uniforms)
    {
        return source.Sksl is null ? null : SkiaPixelShader.Create(source, uniforms);
    }

    /// <inheritdoc/>
    public IColorLut? ImportLut(ColorLut3D lut)
    {
        _thread.Assert();
        return SkiaColorLut.Create(lut);
    }

    /// <summary>
    /// Begins a pass that draws into a dmabuf or a CPU buffer. The renderer caches one Skia surface per
    /// target until the buffer is destroyed. An abandoned context drops every cached target and throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="target">A BGRA dmabuf or a 4-byte memory buffer to draw into.</param>
    /// <param name="options">The fences and the output color description of the pass.</param>
    /// <returns>The renderer's single pass object. The caller must submit it before the next pass begins.</returns>
    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();

        if (_context.IsAbandoned)
        {
            DropTargets();
            throw new InvalidOperationException("The Ganesh context is abandoned; cached targets were dropped.");
        }

        if (_targets.TryGetValue(target, out var entry) && entry.Native.IsCpuReadback && entry.Native.SeenWrites != target.WriteCount)
        {
            _targets.Remove(target);
            ReleaseTarget(entry);
            entry = null;
        }

        entry ??= ImportTarget(target);

        _device.WaitFence(options.WaitFenceFd);
        DirtyGlStateForDebug();

        _context.ResetContext(uint.MaxValue);
        _pass.Begin(target, entry, options.SignalFenceFd, options.ColorDescription);
        return _pass;
    }

    /// <summary>
    /// Tells Ganesh that code outside Skia changed GL state. Call it after raw GL calls on the device
    /// context, before the next Skia draw.
    /// </summary>
    public void NotifyGlStateTouched() => _context.ResetContext(uint.MaxValue);

    private TargetEntry ImportTarget(IBuffer target)
    {
        if (!target.TryGetDmabuf(out _) && target.Format.BytesPerPixel() != 4)
        {
            throw new NotSupportedException($"{target.Format} is not a CPU render target format for the Skia GL renderer.");
        }

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
        native.SeenWrites = target.WriteCount;
        _targets[target] = entry;
        if (_watched.Add(target))
        {
            target.Destroyed += () =>
            {
                _watched.Remove(target);
                if (_targets.Remove(target, out var dead))
                {
                    ReleaseTarget(dead);
                }
            };
        }

        return entry;
    }

    /// <summary>
    /// Frees every cached target and image, abandons the Ganesh context and disposes the GL device.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        DropTargets();
        _device.TextureDeleting -= ForgetBackdropImage;
        foreach (var (backend, image) in _backdropImages.Values)
        {
            SkiaCensus.Release(image);
            SkiaCensus.Release(backend);
        }

        _backdropImages.Clear();
        ColorTransforms.Dispose();
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
