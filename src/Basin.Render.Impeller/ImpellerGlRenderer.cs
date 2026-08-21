using Basin.Diagnostics;
using Basin.Render.Gl;
using NImpeller;

namespace Basin.Render.Impeller;

public sealed unsafe class ImpellerGlRenderer : IRenderer
{
    private readonly GlDevice _device;
    private readonly ImpellerContext _context;
    private readonly IntPtr _contextRaw;
    private readonly IntPtr _rectPaint;
    private readonly IntPtr _texturePaint;
    private readonly Dictionary<IBuffer, TargetEntry> _targets = [];
    private readonly ImpellerGlRenderPass _pass;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    public ImpellerGlRenderer(string devicePath = "/dev/dri/renderD128")
    {
        _device = new GlDevice(devicePath);
        var context = ImpellerContext.CreateOpenGLESNew(
            (IntPtr name) => (IntPtr)Mesa.Native.Libegl.eglGetProcAddress((sbyte*)name));
        if (context is null)
        {
            _device.Dispose();
            throw new InvalidOperationException("Impeller rejected the GLES context on this device.");
        }

        _context = context;
        BasinCounters.Track();
        bool dangerous = false;
        _context.Handle.DangerousAddRef(ref dangerous);
        _contextRaw = _context.Handle.DangerousGetHandle();
        _rectPaint = UnsafeNativeMethods.ImpellerPaintNewRaw();
        _texturePaint = UnsafeNativeMethods.ImpellerPaintNewRaw();
        BasinCounters.Track(2);
        _pass = new ImpellerGlRenderPass(this);
    }

    public GlDevice Device => _device;

    IRenderDevice? IRenderer.Device => Device;

    public static RenderStack CreateStack(string renderNodePath)
    {
        var renderer = new ImpellerGlRenderer(renderNodePath);
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

    internal IntPtr ContextRaw => _contextRaw;

    internal IntPtr RectPaint => _rectPaint;

    internal IntPtr TexturePaint => _texturePaint;

    public DrmFormatSet DmabufTextureFormats => _device.SampleableFormats;

    public ColorTransformCapability ColorTransform => ColorTransformCapability.None;

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
                ? ImpellerGlDmabufTexture.TryImport(this, attributes)
                : null;
        }

        return new ImpellerGlShmTexture(this, buffer);
    }

    public IColorLut? ImportLut(ColorLut3D lut) => null;

    public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options)
    {
        _thread.Assert();

        if (!_targets.TryGetValue(target, out var entry))
        {
            entry = ImportTarget(target);
        }

        _device.WaitFence(options.WaitFenceFd);

        _pass.Begin(target, entry, options.SignalFenceFd);
        return _pass;
    }

    private TargetEntry ImportTarget(IBuffer target)
    {
        var native = GlRenderTarget.Create(_device, target);
        var size = new ImpellerISize { Width = target.Width, Height = target.Height };
        var surface = UnsafeNativeMethods.ImpellerSurfaceCreateWrappedFBONewRaw(
            _contextRaw, native.Framebuffer, ImpellerPixelFormat.kImpellerPixelFormatRGBA8888, &size);
        if (surface == IntPtr.Zero)
        {
            native.Dispose(_device);
            throw new InvalidOperationException("Impeller rejected the render target's framebuffer.");
        }

        BasinCounters.Track();

        const uint GlBgraExt = 0x80E1;
        var attachmentHasAlpha = native.IsCpuReadback
            || !target.TryGetDmabuf(out var targetAttributes)
            || targetAttributes.Format.HasAlpha();
        var gl = _device.Gl;
        var snapshotId = gl.GenTexture();
        gl.BindTexture(Silk.NET.OpenGLES.TextureTarget.Texture2D, snapshotId);
        gl.TexImage2D(
            Silk.NET.OpenGLES.TextureTarget.Texture2D, 0,
            native.IsCpuReadback ? (int)Silk.NET.OpenGLES.InternalFormat.Rgba8
                : attachmentHasAlpha ? (int)GlBgraExt : (int)Silk.NET.OpenGLES.InternalFormat.Rgb8,
            (uint)target.Width, (uint)target.Height, 0,
            native.IsCpuReadback ? Silk.NET.OpenGLES.PixelFormat.Rgba
                : attachmentHasAlpha ? (Silk.NET.OpenGLES.PixelFormat)GlBgraExt : Silk.NET.OpenGLES.PixelFormat.Rgb,
            Silk.NET.OpenGLES.PixelType.UnsignedByte, null);
        var descriptor = new ImpellerTextureDescriptor
        {
            Pixel_format = ImpellerPixelFormat.kImpellerPixelFormatRGBA8888,
            Size = size,
            Mip_count = 1,
        };
        var snapshot = UnsafeNativeMethods.ImpellerTextureCreateWithOpenGLTextureHandleNewRaw(
            _contextRaw, &descriptor, snapshotId);
        if (snapshot == IntPtr.Zero)
        {
            gl.DeleteTexture(snapshotId);
            UnsafeNativeMethods.ImpellerSurfaceRelease(surface);
            BasinCounters.Untrack();
            native.Dispose(_device);
            throw new InvalidOperationException("Impeller rejected the preservation snapshot texture.");
        }

        BasinCounters.Track();
        var entry = new TargetEntry(native, surface, snapshot, snapshotId);
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
        foreach (var entry in _targets.Values)
        {
            ReleaseTarget(entry);
        }

        _targets.Clear();
        RenderFences.CloseFence(_completionFence);
        _completionFence = -1;
        UnsafeNativeMethods.ImpellerPaintRelease(_rectPaint);
        UnsafeNativeMethods.ImpellerPaintRelease(_texturePaint);
        BasinCounters.Untrack(2);

        _context.Handle.DangerousRelease();
        _context.Dispose();
        BasinCounters.Untrack();
        _device.Dispose();
    }

    private void ReleaseTarget(TargetEntry entry)
    {
        UnsafeNativeMethods.ImpellerSurfaceRelease(entry.Surface);
        UnsafeNativeMethods.ImpellerTextureRelease(entry.Snapshot);
        BasinCounters.Untrack(2);
        entry.Native.Dispose(_device);
    }

    internal sealed record TargetEntry(GlRenderTarget Native, IntPtr Surface, IntPtr Snapshot, uint SnapshotGlId);
}
