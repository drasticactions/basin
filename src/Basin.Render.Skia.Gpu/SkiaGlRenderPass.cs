using Basin.Render.Skia;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Pixman;
using SkiaSharp;
using Silk.NET.OpenGLES;

namespace Basin.Render.Skia;

internal sealed unsafe class SkiaGlRenderPass : IRenderPass
{
    private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest);

    private readonly SkiaGlRenderer _renderer;
    private readonly SKPaint _paint;
    private IBuffer? _target;
    private SkiaGlRenderer.TargetEntry? _entry;
    private int _signalFenceFd = -1;

    private readonly List<SkiaGlDmabufTexture> _sampled = [];

    internal SkiaGlRenderPass(SkiaGlRenderer renderer, SKPaint paint)
    {
        _renderer = renderer;
        _paint = paint;
    }

    private ImageDescription? _outputColor;

    internal void Begin(IBuffer target, SkiaGlRenderer.TargetEntry entry, int signalFenceFd, ImageDescription? outputColor)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _outputColor = outputColor;
        _entry = entry;
        _signalFenceFd = signalFenceFd;
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Rect(_entry!.Canvas, _paint, _renderer.ColorTransforms.ConvertRect(color, _outputColor), box, clip);
    }

    public void AddTexture(ITexture texture, in TextureRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (texture is SkiaGlDmabufTexture { SampledThisPass: false } dmabuf)
        {
            dmabuf.SampledThisPass = true;
            _sampled.Add(dmabuf);
        }

        SkiaDraw.Texture(
            _entry!.Canvas, _paint, (ISkiaTexture)texture, options,
            options.Lut is null && options.Shader is null ? _renderer.ColorTransforms.TransformFor(options.ColorDescription, _outputColor) : null);
    }

    public void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Mesh(_entry!.Canvas, _paint, (ISkiaTexture?)texture, vertices, options);
    }

    public void AddShader(IPixelShader shader, in ShaderRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        SkiaDraw.Shader(_entry!.Canvas, _paint, shader, options);
    }

    public void AddBackdropEffect(IBackdropEffect effect, in Box bounds, PixmanRegion32? clip = null, object? key = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (effect is not IGlBackdropEffect glEffect)
        {
            throw new ArgumentException("effect does not belong to this renderer");
        }

        if (bounds.IsEmpty)
        {
            return;
        }

        _renderer.Context.Flush();
        var gl = _renderer.Device.Gl;
        var backdrop = _entry!.Native.ColorTexture;
        if (backdrop == 0)
        {
            backdrop = EnsureBackdropCopy(gl);
        }

        gl.BindVertexArray(0);
        for (var unit = 0u; unit < 3; unit++)
        {
            gl.BindSampler(unit, 0);
        }

        var context = new GlBackdropContext
        {
            Device = _renderer.Device,
            Backdrop = backdrop,
            TargetWidth = _target.Width,
            TargetHeight = _target.Height,
            Bounds = bounds,
            Key = key,
        };
        var recorded = glEffect.Record(in context, out var result);
        _renderer.NotifyGlStateTouched();
        if (!recorded || result.Texture == 0 || result.TextureWidth <= 0 || result.TextureHeight <= 0
            || !_renderer.BackdropImage(result.Texture, result.TextureWidth, result.TextureHeight, out var image))
        {
            return;
        }

        var canvas = _entry.Canvas;
        var srcRect = SKRect.Create(result.Source.X, result.Source.Y, result.Source.Width, result.Source.Height);
        var dstRect = SKRect.Create(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        _paint.BlendMode = SKBlendMode.Src;
        _paint.SetColor(new SKColorF(1f, 1f, 1f, 1f), null);
        if (clip is null)
        {
            canvas.DrawImage(image, srcRect, dstRect, NearestSampling, _paint);
        }
        else
        {
            foreach (var band in RegionRects.Of(clip))
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(band.X1, band.Y1, band.X2, band.Y2), SKClipOperation.Intersect, false);
                canvas.DrawImage(image, srcRect, dstRect, NearestSampling, _paint);
                canvas.Restore();
            }
        }

        _paint.BlendMode = SKBlendMode.SrcOver;
    }

    private uint _backdropCopyTexture;
    private uint _backdropCopyFbo;
    private int _backdropCopyWidth;
    private int _backdropCopyHeight;

    private uint EnsureBackdropCopy(GL gl)
    {
        if (_backdropCopyTexture == 0 || _backdropCopyWidth != _target!.Width || _backdropCopyHeight != _target.Height)
        {
            ReleaseBackdropCopy(gl);
            _backdropCopyTexture = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, _backdropCopyTexture);
            gl.TexStorage2D(TextureTarget.Texture2D, 1, SizedInternalFormat.Rgba8, (uint)_target!.Width, (uint)_target.Height);
            _backdropCopyFbo = gl.GenFramebuffer();
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, _backdropCopyFbo);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _backdropCopyTexture, 0);
            _backdropCopyWidth = _target.Width;
            _backdropCopyHeight = _target.Height;
        }

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _entry!.Native.Framebuffer);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _backdropCopyFbo);
        gl.BlitFramebuffer(
            0, 0, _target!.Width, _target.Height,
            0, 0, _target.Width, _target.Height,
            (uint)GLEnum.ColorBufferBit, BlitFramebufferFilter.Nearest);
        return _backdropCopyTexture;
    }

    internal void ReleaseBackdropCopy(GL gl)
    {
        if (_backdropCopyTexture != 0)
        {
            gl.DeleteTexture(_backdropCopyTexture);
            gl.DeleteFramebuffer(_backdropCopyFbo);
            _backdropCopyTexture = 0;
            _backdropCopyFbo = 0;
        }
    }

    private int _scopedSubmits;

    public bool Submit()
    {
        if (_scopedSubmits < 30)
        {
            _scopedSubmits++;
            return SubmitCore();
        }

        AllocationScope.Begin(region: "SkiaGlSubmit", forgiving: true);
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

        _renderer.Context.Flush(submit: true, synchronous: false);

        var gl = _renderer.Device.Gl;
        if (entry.Native.IsCpuReadback)
        {
            ClearSampled();
            entry.Native.ReadInto(_renderer.Device, target);
        }
        else
        {
            PublishFence(entry, gl);
        }

        if (_signalFenceFd >= 0)
        {
            gl.Finish();
            RenderFences.SignalSyncobjFd(_renderer.Device.DrmFd, _signalFenceFd);
            _signalFenceFd = -1;
        }

        return true;
    }

    private void PublishFence(SkiaGlRenderer.TargetEntry entry, GL gl)
    {
        var fence = _renderer.Device.ExportFence();
        if (fence < 0)
        {
            gl.Flush();
            ClearSampled();
            return;
        }

        _renderer.ReplaceCompletionFence(RenderFences.DuplicateFence(fence));

        RenderFences.PublishFenceTo(entry.Native.Attributes, forWrite: true, fence);
        foreach (var texture in _sampled)
        {
            texture.SampledThisPass = false;
            RenderFences.PublishFenceTo(texture.Native.Attributes, forWrite: false, fence);
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
}
